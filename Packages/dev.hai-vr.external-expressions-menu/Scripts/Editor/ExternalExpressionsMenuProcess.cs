﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Hai.ExternalExpressionsMenu;
using Newtonsoft.Json;
using UnityEngine;
using VRC.Core;
using VRC.Dynamics;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDK3.Dynamics.Contact.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;
using VRC.SDKBase.Editor;
using Object = UnityEngine.Object;

namespace ExternalExpresssionsMenu.Editor
{
    public class ExternalExpresssionsMenuProcess
    {
        private readonly Dictionary<Texture2D, string> _cache = new Dictionary<Texture2D, string>();
        private readonly Regex _avoidPathTraversalInAvtrPipelineName = new Regex(@"^avtr_[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$");
        
        public void ExtractMenu(Transform contextAvatarRootTransform, VRCAvatarDescriptor contextAvatarDescriptor)
        {
            if (!APIUser.IsLoggedIn) return;
            var userId = APIUser.CurrentUser.id;
            
            //

            var pipeline = contextAvatarRootTransform.GetComponent<PipelineManager>();
            if (pipeline == null || pipeline.blueprintId == null) return;

            var avatarId = pipeline.blueprintId;
            if (!_avoidPathTraversalInAvtrPipelineName.IsMatch(avatarId)) return;
            
            if (ContainsPathTraversalElements(userId) || ContainsPathTraversalElements(avatarId))
            {
                // Prevent the remote chance of a path traversal
                return;
            }
            
            //

            var menu = contextAvatarDescriptor.expressionsMenu;
            var parameters = contextAvatarDescriptor.expressionParameters;
            if (menu == null || parameters == null) return;
            
            //

            var contacts = contextAvatarRootTransform.GetComponentsInChildren<VRCContactReceiver>(true)
                .Where(receiver => !string.IsNullOrEmpty(receiver.parameter))
                .ToArray();
            var physBones = contextAvatarRootTransform.GetComponentsInChildren<VRCPhysBone>(true)
                .Where(physBone => !string.IsNullOrEmpty(physBone.parameter))
                .ToArray();
            var visited = VisitAllSubMenus(menu);
            var icons = CollectAllIcons(visited.Keys.ToArray());

            var manifest = new EMManifest
            {
                expressionParameters = DevelopExpressionParameters(parameters.parameters),
                contactParameters = DevelopContactParameters(contacts),
                physBoneParameters = DevelopPhysBoneParameters(physBones),
                menu = DevelopMenu(menu, visited, icons),
                icons = icons.Select(EncodeIcon).ToArray()
            };

            var content = JsonConvert.SerializeObject(manifest, Formatting.Indented, new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore
            });

            // VRChat does not like it when the Avatars/ folder contains files it has not created.
/*
Error      -  [All] Avatar attach failed with exception: System.NullReferenceException: Object reference not set to an instance of an object.
  at ???+AvatarOscConfig.GetByName (System.String name) ... 
  at ???+AvatarOscConfig..ctor (System.String id, System.String name, System.Collections.Generic.IEnumerable`1[T] avatarParameters, ???+AvatarOscConfig copyExistingFrom, System.Int32 paramsHash) ...
  
Error      -  [Behaviour] FATAL ERROR: Couldn't even switch to error avatar!
 */
            {
                var directory = $"/VRChat/VRChat/OSC/{userId}/Hai";
                var fullDirectory = $"{VRC_SdkBuilder.GetLocalLowPath()}{directory}";
                Directory.CreateDirectory(fullDirectory);
            }

            var endbit = $"/VRChat/VRChat/OSC/{userId}/Hai/ExternalExpressionsMenu_{avatarId}.json";
            var path = $"{VRC_SdkBuilder.GetLocalLowPath()}{endbit}";
            var printLocation = $"%LOCALAPPDATA%Low{endbit}"; // Doesn't print the account name to the logs

            File.WriteAllText(path, content);
            Debug.Log($"(ExternalExpressionsMenu) Written file as ExternalExpressionsMenu_{avatarId}.json");
        }

        private static bool ContainsPathTraversalElements(string susStr)
        {
            return susStr.Contains("/") || susStr.Contains("\\") || susStr.Contains(".") || susStr.Contains("*");
        }

        private EMExpression[] DevelopExpressionParameters(VRCExpressionParameters.Parameter[] parameters)
        {
            return parameters
                .Select(parameter => new EMExpression
                {
                    parameter = parameter.name ?? "",
                    type = EMType(parameter.valueType),
                    saved = parameter.saved,
                    synced = parameter.networkSynced,
                    defaultValue = parameter.defaultValue
                })
                // Originally, Expression Parameters assets had by default a list of empty parameters, for the user to fill in.
                // We should exclude those.
                .Where(expression => expression.parameter != "") 
                .OrderBy(expression => expression.parameter)
                .ToArray();
        }

        private string EMType(VRCExpressionParameters.ValueType parameterValueType)
        {
            switch (parameterValueType)
            {
                case VRCExpressionParameters.ValueType.Int: return "Int";
                case VRCExpressionParameters.ValueType.Float: return "Float";
                case VRCExpressionParameters.ValueType.Bool: return "Bool";
                default:
                    throw new ArgumentOutOfRangeException(nameof(parameterValueType), parameterValueType, null);
            }
        }

        private EMContact[] DevelopContactParameters(VRCContactReceiver[] contacts)
        {
            // The user may have multiple Contacts with the same parameter name, with different settings (i.e. mutually exclusive).
            // This can't be helped, so store both.
            return contacts
                .Select(receiver =>
                {
                    var scale = (receiver.rootTransform != null ? receiver.rootTransform : receiver.transform).lossyScale;
                    return new EMContact
                    {
                        parameter = receiver.parameter ?? "",
                        receiverType = EMReceiverType(receiver.receiverType),
                        radius = receiver.radius,
                        lossyScaleX = scale.x,
                        lossyScaleY = scale.y,
                        lossyScaleZ = scale.z,
                    };
                })
                .ToArray();
        }

        private string EMReceiverType(ContactReceiver.ReceiverType receiverType)
        {
            switch (receiverType)
            {
                case ContactReceiver.ReceiverType.Constant: return "Constant";
                case ContactReceiver.ReceiverType.OnEnter: return "OnEnter";
                case ContactReceiver.ReceiverType.Proximity: return "Proximity";
                default:
                    throw new ArgumentOutOfRangeException(nameof(receiverType), receiverType, null);
            }
        }

        private EMPhysBone[] DevelopPhysBoneParameters(VRCPhysBone[] physBones)
        {
            // The user may have multiple PhysBones with the same parameter name, with different settings (i.e. mutually exclusive).
            // This can't be helped, so store both.
            return physBones
                .Select(physBone => new EMPhysBone
                {
                    parameter = physBone.parameter ?? "",
                    maxStretch = physBone.maxStretch,
                    maxSquish = physBone.maxSquish,
                    limitType = EMLimitType(physBone.limitType),
                    maxAngleX = physBone.limitType != VRCPhysBoneBase.LimitType.None ? physBone.maxAngleX : -1f,
                    maxAngleZ = physBone.limitType == VRCPhysBoneBase.LimitType.Polar ? physBone.maxAngleZ : -1f
                })
                .ToArray();
        }

        private string EMLimitType(VRCPhysBoneBase.LimitType limitType)
        {
            switch (limitType)
            {
                case VRCPhysBoneBase.LimitType.None: return "None";
                case VRCPhysBoneBase.LimitType.Angle: return "Angle";
                case VRCPhysBoneBase.LimitType.Hinge: return "Hinge";
                case VRCPhysBoneBase.LimitType.Polar: return "Polar";
                default:
                    throw new ArgumentOutOfRangeException(nameof(limitType), limitType, null);
            }
        }

        private List<Texture2D> CollectAllIcons(VRCExpressionsMenu[] menus)
        {
            return menus
                .SelectMany(menu => menu.controls)
                .SelectMany(control =>
                {
                    var texs = new List<Texture2D>();
                    texs.Add(control.icon);

                    var expectedSubParams = SubParamCount(control.type);
                    if (expectedSubParams > 0 && 0 < control.labels.Length) texs.Add(control.labels[0].icon);
                    if (expectedSubParams > 1 && 1 < control.labels.Length) texs.Add(control.labels[1].icon);
                    if (expectedSubParams > 2 && 2 < control.labels.Length) texs.Add(control.labels[2].icon);
                    if (expectedSubParams > 3 && 3 < control.labels.Length) texs.Add(control.labels[3].icon);

                    return texs;
                })
                .Where(tex => tex != null)
                .Distinct()
                .ToList();
        }

        private EMMenu[] DevelopMenu(VRCExpressionsMenu menu, Dictionary<VRCExpressionsMenu, int> visited, List<Texture2D> icons)
        {
            return BuildFor(menu, visited, icons, new[] { menu });
        }

        private Dictionary<VRCExpressionsMenu, int> VisitAllSubMenus(VRCExpressionsMenu menu)
        {
            var visited = new Dictionary<VRCExpressionsMenu, int>();
            var id = 0;
            
            visited.Add(menu, id);
            id++;
            CollectSubMenus(menu, visited, ref id);
            return visited;
        }

        private EMMenu[] BuildFor(VRCExpressionsMenu menu, Dictionary<VRCExpressionsMenu, int> ids, List<Texture2D> icons, VRCExpressionsMenu[] visitedInChain)
        {
            return menu.controls
                .Select(control =>
                {
                    var isSubMenu = control.type == VRCExpressionsMenu.Control.ControlType.SubMenu;
                    var isRecursive = isSubMenu && visitedInChain.Contains(control.subMenu);
                    var isNonRecursive = isSubMenu && !isRecursive;
                    
                    var ourMenu = new EMMenu
                    {
                        label = control.name ?? "",
                        icon = icons.IndexOf(control.icon),
                        type = ControlType(control.type),
                        parameter = control.parameter.name ?? "",
                        value = control.value,
                        subMenu = isNonRecursive ? BuildFor(control.subMenu, ids, icons, visitedInChain.Concat(new []{ menu }).ToArray()) : null,
                        isSubMenuRecursive = isRecursive,
                        subMenuId = isSubMenu ? ids[control.subMenu] : -1
                    };
                    var expectedSubParams = SubParamCount(control.type);
                    if (expectedSubParams > 0) ourMenu.axis0 = AxisOf(control, 0, icons);
                    if (expectedSubParams > 1) ourMenu.axis1 = AxisOf(control, 1, icons);
                    if (expectedSubParams > 2) ourMenu.axis2 = AxisOf(control, 2, icons);
                    if (expectedSubParams > 3) ourMenu.axis3 = AxisOf(control, 3, icons);
                    
                    return ourMenu;
                })
                .ToArray();
        }

        private static string ControlType(VRCExpressionsMenu.Control.ControlType controlType)
        {
            switch (controlType)
            {
                case VRCExpressionsMenu.Control.ControlType.Button: return "Button";
                case VRCExpressionsMenu.Control.ControlType.Toggle: return "Toggle";
                case VRCExpressionsMenu.Control.ControlType.SubMenu: return "SubMenu";
                case VRCExpressionsMenu.Control.ControlType.TwoAxisPuppet: return "TwoAxisPuppet";
                case VRCExpressionsMenu.Control.ControlType.FourAxisPuppet: return "FourAxisPuppet";
                case VRCExpressionsMenu.Control.ControlType.RadialPuppet: return "RadialPuppet";
                default:
                    throw new ArgumentOutOfRangeException(nameof(controlType), controlType, null);
            }
        }

        private EMAxis AxisOf(VRCExpressionsMenu.Control control, int index, List<Texture2D> icons)
        {
            return new EMAxis
            {
                parameter = index < control.subParameters.Length ? control.subParameters[index].name ?? "" : "",
                label = index < control.labels.Length ? control.labels[index].name ?? "" : "",
                icon = index < control.labels.Length ? icons.IndexOf(control.labels[index].icon) : -1
            };
        }

        private string EncodeIcon(Texture2D iconNullable)
        {
            if (iconNullable == null) return "";
            if (_cache.TryGetValue(iconNullable, out var cached)) return cached;

            if (iconNullable.isReadable)
            {
                var encoded = Convert.ToBase64String(iconNullable.EncodeToPNG());
                _cache[iconNullable] = encoded;
                return encoded;
            }
            else
            {
                var rt = RenderTexture.GetTemporary(iconNullable.width, iconNullable.height);
                Graphics.Blit(iconNullable, rt);
                var tempTex = new Texture2D(rt.width, rt.height);
                
                RenderTexture.active = rt;
                tempTex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tempTex.Apply();
                RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(rt);
                
                var encoded = Convert.ToBase64String(tempTex.EncodeToPNG());
                Object.DestroyImmediate(tempTex);
                
                _cache[iconNullable] = encoded;
                return encoded;
            }
        }

        private static int SubParamCount(VRCExpressionsMenu.Control.ControlType controlType)
        {
            switch (controlType)
            {
                case VRCExpressionsMenu.Control.ControlType.Button: return 0;
                case VRCExpressionsMenu.Control.ControlType.Toggle: return 0;
                case VRCExpressionsMenu.Control.ControlType.SubMenu: return 0;
                case VRCExpressionsMenu.Control.ControlType.TwoAxisPuppet: return 2;
                case VRCExpressionsMenu.Control.ControlType.FourAxisPuppet: return 4;
                case VRCExpressionsMenu.Control.ControlType.RadialPuppet: return 1;
                default:
                    throw new ArgumentOutOfRangeException(nameof(controlType), controlType, null);
            }
        }

        private void CollectSubMenus(VRCExpressionsMenu menu, Dictionary<VRCExpressionsMenu, int> visited, ref int id)
        {
            var subMenus = menu.controls
                .Where(control => control.type == VRCExpressionsMenu.Control.ControlType.SubMenu)
                .Where(control => control.subMenu != null)
                .Select(control => control.subMenu)
                .Where(expressionsMenu =>
                {
                    var notVisited = !visited.ContainsKey(expressionsMenu);
                    return notVisited;
                })
                .Distinct()
                .ToArray();
            
            foreach (var subMenu in subMenus)
            {
                visited.Add(subMenu, id);
                id++;
            }
            // Double foreach: Number IDs in the same menu first
            foreach (var subMenu in subMenus)
            {
                CollectSubMenus(subMenu, visited, ref id);
            }
        }
    }
}