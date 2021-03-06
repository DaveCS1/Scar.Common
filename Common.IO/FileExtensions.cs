using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;

namespace Scar.Common.IO
{
    public static class FileExtensions
    {
        [NotNull]
        public static string GetFreeFileName([NotNull] string filePath)
        {
            if (filePath == null)
            {
                throw new ArgumentNullException(nameof(filePath));
            }

            var count = 1;

            var fileNameOnly = Path.GetFileNameWithoutExtension(filePath);
            var extension = Path.GetExtension(filePath);
            var directoryPath = Path.GetDirectoryName(filePath);
            if (directoryPath == null)
            {
                throw new ArgumentException(nameof(filePath));
            }

            var newFilePath = filePath;

            while (File.Exists(newFilePath))
            {
                var tempFileName = $"{fileNameOnly} ({count++})";
                newFilePath = Path.Combine(directoryPath, tempFileName + extension);
            }

            return newFilePath;
        }

        public static void OpenFile([NotNull] this string filePath)
        {
            if (filePath == null)
            {
                throw new ArgumentNullException(nameof(filePath));
            }

            if (!File.Exists(filePath))
            {
                return;
            }

            Process.Start(filePath);
        }

        public static void OpenFileInExplorer([NotNull] this string filePath)
        {
            if (filePath == null)
            {
                throw new ArgumentNullException(nameof(filePath));
            }

            if (!File.Exists(filePath))
            {
                return;
            }

            new Process
            {
                StartInfo =
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{filePath}\""
                }
            }.Start();
        }

        [NotNull]
        [ItemNotNull]
        public static async Task<byte[]> ReadFileAsync([NotNull] this string filename, CancellationToken cancellationToken)
        {
            using (var file = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true))
            {
                var buff = new byte[file.Length];
                await file.ReadAsync(buff, 0, (int)file.Length, cancellationToken).ConfigureAwait(false);
                return buff;
            }
        }

        [NotNull]
        public static string RenameFile([NotNull] this string oldFilePath, [NotNull] string newFilePath)
        {
            if (oldFilePath == null)
            {
                throw new ArgumentNullException(nameof(oldFilePath));
            }

            if (newFilePath == null)
            {
                throw new ArgumentNullException(nameof(newFilePath));
            }

            newFilePath = GetFreeFileName(newFilePath);
            File.Move(oldFilePath, newFilePath);
            return newFilePath;
        }
    }
}