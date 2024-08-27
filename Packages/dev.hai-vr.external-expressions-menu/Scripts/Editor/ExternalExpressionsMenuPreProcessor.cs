#if !EEM_NDMF_IS_INSTALLED
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase.Editor.BuildPipeline;

namespace ExternalExpresssionsMenu.Editor
{
    public class ExternalExpressionsMenuPreProcessor : IVRCSDKPreprocessAvatarCallback
    {
        // Arbitrary number. Run as late as possible. We don't rely on IEditorOnly stuff so there's no concerns about -1024,
        // as long as this runs after other non-destructive processors.
        public int callbackOrder => 12000;
        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            var process = new ExternalExpresssionsMenuProcess();
            process.ExtractMenu(avatarGameObject.transform, avatarGameObject.GetComponent<VRCAvatarDescriptor>());
            return true;
        }
    }
}
#endif