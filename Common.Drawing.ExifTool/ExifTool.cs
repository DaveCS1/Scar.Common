using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Common.Logging;
using JetBrains.Annotations;
using Scar.Common.Drawing.Metadata;
using Scar.Common.Events;
using Scar.Common.Processes;

namespace Scar.Common.Drawing.ExifTool
{
    public sealed class ExifTool : IExifTool, IDisposable
    {
        private static readonly Regex ProgressRegex = new Regex(@"======== (.*) \[(\d+)\/(\d+)\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex ErrorRegex = new Regex(@"Error\: (.*) - (.*)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly string[] MessageSplitters =
        {
            Environment.NewLine
        };

        /// <summary>
        /// Allows only one exif operation at a time
        /// //TODO: Allow simultaneous operations for different paths? - dictionary of semaphores
        /// </summary>
        [NotNull]
        private readonly SemaphoreSlim _exifOperationSemaphore = new SemaphoreSlim(1, 1);

        [NotNull]
        private readonly string _exifToolPath = "exiftool.exe";

        [NotNull]
        private readonly ILog _logger;

        [NotNull]
        private readonly IProcessUtility _processUtility;
        //TODO: TEST BMP, png etc

        public ExifTool([NotNull] IProcessUtility processUtility, [NotNull] ILog logger)
        {
            _processUtility = processUtility ?? throw new ArgumentNullException(nameof(processUtility));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _processUtility.ProcessMessageFired += ProcessUtility_ProcessMessageFired;
            _processUtility.ProcessErrorFired += ProcessUtility_ProcessErrorFired;
        }

        public void Dispose()
        {
            _processUtility.ProcessMessageFired -= ProcessUtility_ProcessMessageFired;
            _processUtility.ProcessErrorFired -= ProcessUtility_ProcessErrorFired;
            _exifOperationSemaphore.Dispose();
        }

        public event EventHandler<FilePathProgressEventArgs> Progress;

        public event EventHandler<FilePathErrorEventArgs> Error;

        public async Task SetOrientationAsync(Orientation orientation, string[] paths, bool backup, CancellationToken token)
        {
            if (paths == null)
            {
                throw new ArgumentNullException(nameof(paths));
            }

            _logger.Info($"Setting orientation to {orientation} for {paths.Length} paths...");
            await PerformExifOperation(paths, backup, $"-Orientation={(int)orientation}", token).ConfigureAwait(false);
        }

        public async Task SetOrientationAsync(Orientation orientation, string path, bool backup, CancellationToken token)
        {
            await SetOrientationAsync(
                    orientation,
                    new[]
                    {
                        path
                    },
                    backup,
                    token)
                .ConfigureAwait(false);
        }

        public async Task ShiftDateAsync(TimeSpan shiftBy, bool plus, string[] paths, bool backup, CancellationToken token)
        {
            if (paths == null)
            {
                throw new ArgumentNullException(nameof(paths));
            }

            var sign = GetSign(plus);
            _logger.Info($"Shifting date by {sign}{shiftBy} for {paths.Length} paths...");
            await PerformExifOperation(paths, backup, $"-AllDates{sign}=\"{shiftBy:dd\\ hh\\:mm\\:ss}\"", token).ConfigureAwait(false);
        }

        public async Task ShiftDateAsync(TimeSpan shiftBy, bool plus, string path, bool backup, CancellationToken token)
        {
            await ShiftDateAsync(
                    shiftBy,
                    plus,
                    new[]
                    {
                        path
                    },
                    backup,
                    token)
                .ConfigureAwait(false);
        }

        [NotNull]
        private static string DecodeFromUtf8([NotNull] string path)
        {
            return Encoding.UTF8.GetString(Encoding.Default.GetBytes(path));
        }

        [NotNull]
        private static string EncodeToUtf8([NotNull] string path)
        {
            return Encoding.Default.GetString(Encoding.UTF8.GetBytes(path));
        }

        [NotNull]
        private static string GetSign(bool plus)
        {
            return plus ? "+" : "-";
        }

        private void OnError([NotNull] FilePathErrorEventArgs eventArgs)
        {
            Error?.Invoke(this, eventArgs);
        }

        private void OnProgress([NotNull] FilePathProgressEventArgs eventArgs)
        {
            Progress?.Invoke(this, eventArgs);
        }

        private async Task PerformExifOperation([NotNull] string[] paths, bool backup, [NotNull] string operation, CancellationToken token)
        {
            if (paths == null)
            {
                throw new ArgumentNullException(nameof(paths));
            }

            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            var backupArg = backup ? null : " -overwrite_original -progress";
            await _exifOperationSemaphore.WaitAsync(token).ConfigureAwait(false);
            //By default exif tool receives ??? instead of valid cyrillic paths - need to reencode
            var encodedPaths = paths.Select(path => $"\"{EncodeToUtf8(path)}\"");
            var command = $"-charset filename=utf8 {operation}{backupArg} -n {string.Join(" ", encodedPaths)}";
            try
            {
                var processResult = await _processUtility.ExecuteCommandAsync(_exifToolPath, command, token).ConfigureAwait(false);
                if (processResult.IsError)
                {
                    throw new InvalidOperationException(processResult.Error);
                }
            }
            finally
            {
                _exifOperationSemaphore.Release();
            }
        }

        private void ProcessUtility_ProcessErrorFired(object sender, [NotNull] EventArgs<string> e)
        {
            _logger.Debug($"Received error from process utility: {e.Parameter}");
            var messages = e.Parameter.Split(MessageSplitters, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim());
            foreach (var message in messages)
            {
                _logger.Trace($"Processing {message}...");
                var match = ErrorRegex.Match(message);
                if (!match.Success)
                {
                    continue;
                }

                var groups = match.Groups;
                var filePath = DecodeFromUtf8(groups[2].Value).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
                var errorMessage = groups[1].Value;
                var eventArgs = new FilePathErrorEventArgs(errorMessage, filePath);
                _logger.Trace($"Reporting error: {eventArgs}...");
                OnError(eventArgs);
            }
        }

        private void ProcessUtility_ProcessMessageFired(object sender, [NotNull] EventArgs<string> e)
        {
            _logger.Debug($"Received message from process utility: {e.Parameter}");
            var messages = e.Parameter.Split(MessageSplitters, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim());
            foreach (var message in messages)
            {
                _logger.Trace($"Processing {message}...");
                var match = ProgressRegex.Match(message);
                if (!match.Success)
                {
                    continue;
                }

                var groups = match.Groups;
                var filePath = DecodeFromUtf8(groups[1].Value).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
                var current = int.Parse(groups[2].Value);
                var total = int.Parse(groups[3].Value);
                _logger.Trace($"Progress detected:  {current} of {total}");
                if (current > total)
                {
                    _logger.Warn("Incorrect percentage");
                    return;
                }

                var eventArgs = new FilePathProgressEventArgs(current, total, filePath);
                _logger.Trace($"Reporting progress: {eventArgs}...");
                OnProgress(eventArgs);
            }
        }
    }
}