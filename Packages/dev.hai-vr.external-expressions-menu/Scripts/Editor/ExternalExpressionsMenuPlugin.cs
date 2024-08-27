#if EEM_NDMF_IS_INSTALLED
using System;
using ExternalExpresssionsMenu.Editor;
using nadena.dev.ndmf;
using UnityEditor;
using UnityEngine;

[assembly: ExportsPlugin(typeof(ExternalExpresssionsMenuPlugin))]
namespace ExternalExpresssionsMenu.Editor
{
    public class ExternalExpresssionsMenuPlugin : Plugin<ExternalExpresssionsMenuPlugin>
    {
        public override string QualifiedName => "dev.hai-vr.external-expressions-menu.ExternalExpresssionsMenuPlugin";
        public override string DisplayName => "External Expressions Menu - Extract";

        protected override void Configure()
        {
            var seq = InPhase(BuildPhase.Optimizing)
                .BeforePlugin("nadena.dev.modular-avatar");

            seq.Run("Extract menu", ExtractMenu);
        }

        private void ExtractMenu(BuildContext context)
        {
            try
            {
                DoExtractMenu(context);
            }
            catch (Exception e)
            {
                Debug.Log("(ExternalExpressionsMenu) Extraction failed!");
                Debug.LogException(e);
            }
        }

        private void DoExtractMenu(BuildContext context)
        {
            var contextAvatarRootTransform = context.AvatarRootTransform;
            var contextAvatarDescriptor = context.AvatarDescriptor;

            if (EditorApplication.isPlaying) return;

            var process = new ExternalExpresssionsMenuProcess();
            process.ExtractMenu(contextAvatarRootTransform, contextAvatarDescriptor);
        }
    }
}
#endif