using ReactUnity.Styling;
using ReactUnity.Types;
using UnityEngine;
using UnityEngine.Video;

namespace ReactUnity.Components
{
    public class VideoComponent : RenderTextureComponent
    {
        public VideoPlayer VideoPlayer;

        public VideoComponent(UnityUGUIContext context) : base(context, "video")
        {
            VideoPlayer = AddComponent<VideoPlayer>();
            VideoPlayer.renderMode = VideoRenderMode.RenderTexture;
            VideoPlayer.targetTexture = RenderTexture;

            VideoPlayer.prepareCompleted += PrepareCompleted;
        }

        private void PrepareCompleted(VideoPlayer source)
        {
            RenderTexture.width = (int) source.width;
            RenderTexture.height = (int) source.height;
            Measurer.MarkDirty();
        }

        public override void SetProperty(string propertyName, object value)
        {
            switch (propertyName)
            {
                case "source":
                    SetSource(ParserMap.VideoReferenceConverter.Convert(value) as VideoReference);
                    return;
                default:
                    base.SetProperty(propertyName, value);
                    break;
            }
        }

        private void SetSource(VideoReference source)
        {
            source.Get(Context, (res) =>
            {
                if (res == null)
                {
                    VideoPlayer.clip = null;
                    VideoPlayer.url = null;
                    VideoPlayer.source = VideoSource.Url;
                }
                else
                {
                    VideoPlayer.clip = res.Clip;
                    VideoPlayer.url = res.Url;
                    VideoPlayer.source = res.Type;
                }
            });
        }
    }

    public class VideoComponentSource
    {
        public string Url;
        public VideoClip Clip;
        public VideoSource Type;
    }
}
