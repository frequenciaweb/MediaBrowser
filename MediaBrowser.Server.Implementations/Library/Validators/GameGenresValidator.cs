﻿using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Logging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MediaBrowser.Server.Implementations.Library.Validators
{
    class GameGenresValidator
    {
        /// <summary>
        /// The _library manager
        /// </summary>
        private readonly ILibraryManager _libraryManager;

        /// <summary>
        /// The _user manager
        /// </summary>
        private readonly IUserManager _userManager;

        /// <summary>
        /// The _logger
        /// </summary>
        private readonly ILogger _logger;

        public GameGenresValidator(ILibraryManager libraryManager, IUserManager userManager, ILogger logger)
        {
            _libraryManager = libraryManager;
            _userManager = userManager;
            _logger = logger;
        }

        /// <summary>
        /// Runs the specified progress.
        /// </summary>
        /// <param name="progress">The progress.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task Run(IProgress<double> progress, CancellationToken cancellationToken)
        {
            var items = _libraryManager.RootFolder.RecursiveChildren.Where(i => (i is Game))
                .SelectMany(i => i.Genres)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            progress.Report(2);
            var numComplete = 0;
            var count = items.Count;

            foreach (var name in items)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var itemByName = _libraryManager.GetGameGenre(name);

                    await itemByName.RefreshMetadata(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Don't clutter the log
                    break;
                }
                catch (Exception ex)
                {
                    _logger.ErrorException("Error refreshing {0}", ex, name);
                }

                numComplete++;
                double percent = numComplete;
                percent /= count;
                percent *= 90;

                progress.Report(percent + 10);
            }

            progress.Report(100);
        }
    }
}
