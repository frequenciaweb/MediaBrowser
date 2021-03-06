﻿using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Net;
using MediaBrowser.Model.Session;
using MediaBrowser.Model.System;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MediaBrowser.Server.Implementations.Session
{
    public class WebSocketController : ISessionController, IDisposable
    {
        public SessionInfo Session { get; private set; }
        public IReadOnlyList<IWebSocketConnection> Sockets { get; private set; }

        private readonly ILogger _logger;

        private readonly ISessionManager _sessionManager;

        public WebSocketController(SessionInfo session, ILogger logger, ISessionManager sessionManager)
        {
            Session = session;
            _logger = logger;
            _sessionManager = sessionManager;
            Sockets = new List<IWebSocketConnection>();
        }

        public bool IsSessionActive
        {
            get
            {
                return Sockets.Any(i => i.State == WebSocketState.Open);
            }
        }

        public bool SupportsMediaControl
        {
            get { return GetActiveSockets().Any(); }
        }

        private IEnumerable<IWebSocketConnection> GetActiveSockets()
        {
            return Sockets
                .OrderByDescending(i => i.LastActivityDate)
                .Where(i => i.State == WebSocketState.Open);
        }

        public void AddWebSocket(IWebSocketConnection connection)
        {
            var sockets = Sockets.ToList();
            sockets.Add(connection);

            Sockets = sockets;

            connection.Closed += connection_Closed;
        }

        void connection_Closed(object sender, EventArgs e)
        {
            if (!GetActiveSockets().Any())
            {
                try
                {
                    _sessionManager.ReportSessionEnded(Session.Id);
                }
                catch (Exception ex)
                {
                    _logger.ErrorException("Error reporting session ended.", ex);
                }
            }
            else
            {
                var capabilities = new ClientCapabilities
                {
                    PlayableMediaTypes = Session.PlayableMediaTypes,
                    SupportedCommands = Session.SupportedCommands,
                    SupportsMediaControl = SupportsMediaControl
                };

                _sessionManager.ReportCapabilities(Session.Id, capabilities);
            }
        }

        private IWebSocketConnection GetActiveSocket()
        {
            var socket = GetActiveSockets()
                .FirstOrDefault();

            if (socket == null)
            {
                throw new InvalidOperationException("The requested session does not have an open web socket.");
            }

            return socket;
        }

        public Task SendPlayCommand(PlayRequest command, CancellationToken cancellationToken)
        {
            return SendMessage(new WebSocketMessage<PlayRequest>
            {
                MessageType = "Play",
                Data = command

            }, cancellationToken);
        }

        public Task SendPlaystateCommand(PlaystateRequest command, CancellationToken cancellationToken)
        {
            return SendMessage(new WebSocketMessage<PlaystateRequest>
            {
                MessageType = "Playstate",
                Data = command

            }, cancellationToken);
        }

        public Task SendLibraryUpdateInfo(LibraryUpdateInfo info, CancellationToken cancellationToken)
        {
            return SendMessages(new WebSocketMessage<LibraryUpdateInfo>
            {
                MessageType = "LibraryChanged",
                Data = info

            }, cancellationToken);
        }

        /// <summary>
        /// Sends the restart required message.
        /// </summary>
        /// <param name="info">The information.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>Task.</returns>
        public Task SendRestartRequiredNotification(SystemInfo info, CancellationToken cancellationToken)
        {
            return SendMessages(new WebSocketMessage<SystemInfo>
            {
                MessageType = "RestartRequired",
                Data = info

            }, cancellationToken);
        }


        /// <summary>
        /// Sends the user data change info.
        /// </summary>
        /// <param name="info">The info.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>Task.</returns>
        public Task SendUserDataChangeInfo(UserDataChangeInfo info, CancellationToken cancellationToken)
        {
            return SendMessages(new WebSocketMessage<UserDataChangeInfo>
            {
                MessageType = "UserDataChanged",
                Data = info

            }, cancellationToken);
        }

        /// <summary>
        /// Sends the server shutdown notification.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>Task.</returns>
        public Task SendServerShutdownNotification(CancellationToken cancellationToken)
        {
            return SendMessages(new WebSocketMessage<string>
            {
                MessageType = "ServerShuttingDown",
                Data = string.Empty

            }, cancellationToken);
        }

        /// <summary>
        /// Sends the server restart notification.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>Task.</returns>
        public Task SendServerRestartNotification(CancellationToken cancellationToken)
        {
            return SendMessages(new WebSocketMessage<string>
            {
                MessageType = "ServerRestarting",
                Data = string.Empty

            }, cancellationToken);
        }

        public Task SendGeneralCommand(GeneralCommand command, CancellationToken cancellationToken)
        {
            return SendMessage(new WebSocketMessage<GeneralCommand>
            {
                MessageType = "GeneralCommand",
                Data = command

            }, cancellationToken);
        }

        public Task SendSessionEndedNotification(SessionInfoDto sessionInfo, CancellationToken cancellationToken)
        {
            return SendMessages(new WebSocketMessage<SessionInfoDto>
            {
                MessageType = "SessionEnded",
                Data = sessionInfo

            }, cancellationToken);
        }

        public Task SendPlaybackStartNotification(SessionInfoDto sessionInfo, CancellationToken cancellationToken)
        {
            return SendMessages(new WebSocketMessage<SessionInfoDto>
            {
                MessageType = "PlaybackStart",
                Data = sessionInfo

            }, cancellationToken);
        }

        public Task SendPlaybackStoppedNotification(SessionInfoDto sessionInfo, CancellationToken cancellationToken)
        {
            return SendMessages(new WebSocketMessage<SessionInfoDto>
            {
                MessageType = "PlaybackStopped",
                Data = sessionInfo

            }, cancellationToken);
        }

        private Task SendMessage<T>(WebSocketMessage<T> message, CancellationToken cancellationToken)
        {
            if (SkipSending()) return Task.FromResult(true);

            var socket = GetActiveSocket();

            return socket.SendAsync(message, cancellationToken);
        }

        private Task SendMessages<T>(WebSocketMessage<T> message, CancellationToken cancellationToken)
        {
            if (SkipSending()) return Task.FromResult(true);

            var tasks = GetActiveSockets().Select(i => Task.Run(async () =>
            {
                try
                {
                    await i.SendAsync(message, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.ErrorException("Error sending web socket message", ex);
                }

            }, cancellationToken));

            return Task.WhenAll(tasks);
        }

        private bool SkipSending()
        {
            if (Session != null)
            {
                if (string.Equals(Session.Client, "mb-classic", StringComparison.OrdinalIgnoreCase))
                {
                    Version version;

                    if (!string.IsNullOrWhiteSpace(Session.ApplicationVersion) && Version.TryParse(Session.ApplicationVersion, out version))
                    {
                        if (version < new Version(3, 0, 196))
                        {
                            _logger.Debug("Skipping web socket message to MBC version {0}.", version);
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        public void Dispose()
        {
            foreach (var socket in Sockets.ToList())
            {
                socket.Closed -= connection_Closed;
            }
        }
    }
}
