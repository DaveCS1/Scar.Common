using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Common.Logging;
using JetBrains.Annotations;
using Scar.Common.Drawing.Metadata;
using Scar.Common.IO;

namespace Scar.Common.Drawing.ImageRetriever
{
    public class ImageRetriever : IImageRetriever
    {
        private static readonly TimeSpan DefaultAttemptDelay = TimeSpan.FromMilliseconds(100);

        [NotNull]
        private readonly ILog _logger;

        public ImageRetriever([NotNull] ILog logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<byte[]> GetThumbnailAsync(string filePath, CancellationToken cancellationToken)
        {
            Func<AttemptInfo, Task<byte[]>> loadFileTaskFactory = attemptInfo => filePath.ReadFileAsync(cancellationToken);
            //TODO: token?
            var bytes = await loadFileTaskFactory.RunTaskWithSeveralAttemptsAsync(
                    (attemptInfo, e) =>
                    {
                        if (e is IOException)
                        {
                            var attemptLog = attemptInfo.HasAttempts ? $"Retrying ({attemptInfo})..." : "No more attempts left";
                            _logger.Debug($"Failed loading thumbnail for {filePath} with IO exception. {attemptLog}");
                            return true;
                        }

                        _logger.Warn($"Cannot load thumbnail for {filePath}", e);
                        return false;
                    },
                    DefaultAttemptDelay,
                    throwOnAttemptLimit: true)
                .ConfigureAwait(false);
            Image image;
            using (var mem = new MemoryStream(bytes))
            {
                mem.Position = 0;
                image = Image.FromStream(mem);
            }

            var ratio = image.Width / (double)image.Height;
            const int thumbSize = 120;
            return ImageToByteArray(image.GetThumbnailImage(thumbSize, (int)(thumbSize / ratio), () => false, IntPtr.Zero));
        }

        public async Task<BitmapSource> LoadImageAsync(byte[] imageData, CancellationToken cancellationToken, Orientation? orientation = null, int sizeAnchor = 0)
        {
            return await Task.Run(
                    () =>
                    {
                        if (imageData == null || imageData.Length == 0)
                        {
                            return null;
                        }

                        var image = new BitmapImage();
                        using (var mem = new MemoryStream(imageData))
                        {
                            mem.Position = 0;
                            image.BeginInit();
                            image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                            image.CacheOption = BitmapCacheOption.OnLoad;
                            if (sizeAnchor > 0)
                            {
                                image.DecodePixelWidth = sizeAnchor;
                            }

                            image.UriSource = null;
                            image.StreamSource = mem;
                            image.EndInit();
                        }

                        cancellationToken.ThrowIfCancellationRequested();
                        image.Freeze();

                        if (orientation == null)
                        {
                            return image;
                        }

                        var angle = GetAngleByOrientation(orientation);

                        if (angle == 0)
                        {
                            return image;
                        }

                        return ApplyRotateTransform(angle, image);
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }

        public BitmapSource ApplyRotateTransform(int angle, BitmapSource image)
        {
            var transformedBitmap = new TransformedBitmap();
            transformedBitmap.BeginInit();
            transformedBitmap.Source = image ?? throw new ArgumentNullException(nameof(image));
            var transform = new RotateTransform(angle);
            transformedBitmap.Transform = transform;
            transformedBitmap.EndInit();

            transformedBitmap.Freeze();
            return transformedBitmap;
        }

        private static int GetAngleByOrientation(Orientation? orientation)
        {
            var angle = 0;
            switch (orientation)
            {
                case Orientation.NotSpecified:
                case Orientation.Straight:
                    break;
                case Orientation.Reverse:
                    angle = 180;
                    break;
                case Orientation.Left:
                    angle = 90;
                    break;
                case Orientation.Right:
                    angle = 270;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(orientation), orientation, null);
            }

            return angle;
        }

        [NotNull]
        private static byte[] ImageToByteArray([NotNull] Image imageIn)
        {
            using (var ms = new MemoryStream())
            {
                imageIn.Save(ms, ImageFormat.Gif);
                return ms.ToArray();
            }
        }
    }
}