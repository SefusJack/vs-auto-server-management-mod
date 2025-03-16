using System;
using System.IO;
using System.Timers;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.API.Config;
using System.Collections.Generic;

namespace AutoServerManagement
{
    public class AutoServerManagementController : ModSystem
    {
        private ICoreServerAPI sapi;
        private Timer backupTimer;
        private string backupFolder = Path.Combine(GamePaths.DataPath, "Backups");

        private DateTime lastSaveTime = DateTime.Now;
        // Number of minutes after which we force a /save before backups
        private const double saveThresholdMinutes = 30;

        /// <summary>
        /// Determines whether this mod system should load on the given side (Client/Server).
        /// Only loads on the server side.
        /// </summary>
        public override bool ShouldLoad(EnumAppSide forSide)
        {
            // Only load on the server side
            return forSide == EnumAppSide.Server;
        }

        /// <summary>
        /// Called when the server side of this mod system starts.
        /// Registers the "/autoserver" command and initiates the automatic backups on launch.
        /// </summary>
        public override void StartServerSide(ICoreServerAPI api)
        {
            base.StartServerSide(api);
            sapi = api;
            // Register a command: /autoserver start <minutes> or /autoserver stop
            api.ChatCommands
               .Create("autoserver")
               .WithDescription("Start or stop auto-backups. Usage: /autoserver start <minutes> or /autoserver stop.")
               .RequiresPrivilege(Privilege.controlserver)
               .IgnoreAdditionalArgs()
               .HandleWith(OnAutoServerCommand);
            // Automatically start backups every 30 minutes on server launch
            StartAutoBackups(60);
        }

        /// <summary>
        /// Handles the "/autoserver" command logic. 
        /// Allows starting or stopping the auto-backup timer and sets the interval in minutes.
        /// </summary>
        private TextCommandResult OnAutoServerCommand(TextCommandCallingArgs args)
        {
            var player = args.Caller as IServerPlayer;
            string firstArg = args.RawArgs.PopWord(); // "start" or "stop"
            if (string.IsNullOrEmpty(firstArg))
            {
                MessageCaller(player, "Usage: /autoserver start <minutes> or /autoserver stop");
                return TextCommandResult.Success();
            }

            if (firstArg.Equals("start", StringComparison.OrdinalIgnoreCase))
            {
                int? maybeMinutes = args.RawArgs.PopInt();
                int minutes = (maybeMinutes.HasValue && maybeMinutes.Value > 0) ? maybeMinutes.Value : 30;
                StartAutoBackups(minutes);
                MessageCaller(player, $"Auto-backups started every {minutes} minutes.");
            }
            else if (firstArg.Equals("stop", StringComparison.OrdinalIgnoreCase))
            {
                StopAutoBackups();
                MessageCaller(player, "Auto-backups stopped.");
            }
            else
            {
                MessageCaller(player, "Usage: /autoserver start <minutes> or /autoserver stop");
                return TextCommandResult.Error("Invalid subcommand");
            }

            return TextCommandResult.Success();
        }

        /// <summary>
        /// Starts the auto-backup timer, scheduling backups at the specified interval (in minutes).
        /// </summary>
        private void StartAutoBackups(int minutes)
        {
            StopAutoBackups();

            backupTimer = new Timer(minutes * 60_000);
            backupTimer.Elapsed += OnBackupTimerElapsed;
            backupTimer.AutoReset = true;
            backupTimer.Start();

            sapi.Logger.Notification($"[Auto Server Management] Auto-backups enabled (every {minutes} min).");
        }

        /// <summary>
        /// Stops the currently running auto-backup timer, if any.
        /// </summary>
        private void StopAutoBackups()
        {
            if (backupTimer != null)
            {
                backupTimer.Stop();
                backupTimer.Dispose();
                backupTimer = null;
            }
            sapi.Logger.Notification("[Auto Server Management] Auto-backups disabled.");
        }

        /// <summary>
        /// Event handler for when the auto-backup timer elapses. 
        /// Attempts to perform a backup by calling DoBackup().
        /// </summary>
        private void OnBackupTimerElapsed(object sender, ElapsedEventArgs e)
        {
            try
            {
                DoBackup();
            }
            catch (Exception ex)
            {
                sapi.Logger.Error("[Auto Server Management] Backup failed: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Forces a /save if available. Once completed, it calls onComplete().
        /// </summary>
        private void ForceSave(Action onComplete)
        {
            var saveCmd = sapi.ChatCommands.Get("save");
            if (saveCmd == null)
            {
                return;
            }

            var consoleCaller = new Caller
            {
                Type = EnumCallerType.Console,
                CallerPrivileges = new string[] { Privilege.controlserver }
            };

            var callArgs = new TextCommandCallingArgs
            {
                Caller = consoleCaller,
                RawArgs = new CmdArgs("")
            };

            // Execute /save as console
            saveCmd.Execute(callArgs, (result) =>
            {
                if (result.Status == EnumCommandStatus.Success)
                {
                    sapi.Logger.Notification("[Auto Server Management] Successfully forced /save via consoleCaller.");
                }
                else
                {
                    sapi.Logger.Warning("[Auto Server Management] /save command failed: " + result.StatusMessage);
                }

                // Done saving, invoke the callback to continue
                onComplete?.Invoke();
            });
        }

        /// <summary>
        /// Actually runs the /genbackup command, then prunes old backups.
        /// </summary>
        private void RunGenBackup()
        {
            var command = sapi.ChatCommands.Get("genbackup");
            if (command == null)
            {
                sapi.Logger.Error("[Auto Server Management] The built-in /genbackup command was not found.");
                return;
            }

            var consoleCaller = new Caller
            {
                Type = EnumCallerType.Console,
                CallerPrivileges = new string[] { Privilege.controlserver }
            };

            var callArgs = new TextCommandCallingArgs
            {
                Caller = consoleCaller,
                RawArgs = new CmdArgs("")
            };

            command.Execute(callArgs, (result) =>
            {
                if (result.Status == EnumCommandStatus.Success)
                {
                    sapi.Logger.Notification("[Auto Server Management] Successfully ran /genbackup command.");
                    // Broadcast a chat message to all players
                    sapi.SendMessageToGroup(
                        GlobalConstants.ServerInfoChatGroup,
                        $"[{DateTime.Now:d.M.yyyy HH:mm:ss}][Auto Server Management] World backup completed successfully!",
                        EnumChatType.Notification
                    );

                    // Cleanup old backups
                    RemoveOldBackups();
                }
                else
                {
                    sapi.Logger.Error("[Auto Server Management] /genbackup command failed: " + result.StatusMessage);
                    sapi.SendMessageToGroup(
                        GlobalConstants.ServerInfoChatGroup,
                        $"[{DateTime.Now:d.M.yyyy HH:mm:ss}][Auto Server Management] World backup failed! Please contact the server admin",
                        EnumChatType.Notification
                    );
                }
            });
        }

        /// <summary>
        /// Calls the built-in /genbackup command (via RunGenBackup) but first checks if enough time
        /// has passed since the last forced save. If needed, invokes ForceSave() before the backup.
        /// </summary>
        private void DoBackup()
        {
            double minutesSinceLastSave = (DateTime.Now - lastSaveTime).TotalMinutes;

            // If it's been longer than the threshold, force a /save first
            if (minutesSinceLastSave > saveThresholdMinutes)
            {
                ForceSave(() =>
                {
                    // Update lastSaveTime to now
                    lastSaveTime = DateTime.Now;
                    // Then run /genbackup
                    RunGenBackup();
                });
            }
            else
            {
                // Otherwise just do /genbackup immediately
                RunGenBackup();
            }
        }

        private void RemoveOldBackups()
        {
            if (!Directory.Exists(backupFolder)) return;

            // Gather all backup files
            // Adjust the pattern to match how /genbackup names them
            // e.g. "*.vcdbs" or "*.zip" or "MyWorldName*.vcdbs"
            string[] backupFiles = Directory.GetFiles(backupFolder, $"{Path.GetFileNameWithoutExtension(sapi.WorldManager.CurrentWorldName)}*.vcdbs", SearchOption.TopDirectoryOnly);
            if (backupFiles.Length == 0) return;

            // Sort by creation time DESC (newest first)
            // So backupFiles[0] is the newest, last is the oldest.
            Array.Sort(backupFiles, (a, b) =>
            {
                DateTime ta = File.GetCreationTime(a);
                DateTime tb = File.GetCreationTime(b);
                // We want newest first => compare in reverse
                return tb.CompareTo(ta);
            });

            // We'll track which files we want to keep in a HashSet
            var keepers = new HashSet<string>();

            // Always keep the 3 newest
            int keepNewestCount = Math.Min(3, backupFiles.Length);
            for (int i = 0; i < keepNewestCount; i++)
            {
                keepers.Add(backupFiles[i]);
            }

            // We'll look for 1 day, 1 week, 1 month old backups
            bool foundDay = false;
            bool foundWeek = false;
            bool foundMonth = false;

            DateTime now = DateTime.Now;
            TimeSpan oneDay = TimeSpan.FromDays(1);
            TimeSpan oneWeek = TimeSpan.FromDays(7);
            TimeSpan oneMonth = TimeSpan.FromDays(30); // Simplified "month"

            // Scan the entire list from newest to oldest
            // looking for the first backup that crosses each threshold
            // Because it's sorted newest -> oldest, the first one that meets each threshold is the "newest" that does so.
            for (int i = 0; i < backupFiles.Length; i++)
            {
                var file = backupFiles[i];
                DateTime ctime = File.GetCreationTime(file);
                TimeSpan age = now - ctime;

                // If we haven't found a day-old backup yet, and this file is >= 1 day old
                if (!foundDay && age >= oneDay)
                {
                    keepers.Add(file);
                    foundDay = true;
                }
                // If we haven't found a week-old backup yet, and this file is >= 1 week old
                if (!foundWeek && age >= oneWeek)
                {
                    keepers.Add(file);
                    foundWeek = true;
                }
                // If we haven't found a month-old backup yet, and this file is >= 1 month old
                if (!foundMonth && age >= oneMonth)
                {
                    keepers.Add(file);
                    foundMonth = true;
                }

                // If we've found all three, no need to keep scanning
                if (foundDay && foundWeek && foundMonth) break;
            }

            // If we never found a day-old/week-old/month-old backup,
            // keep the oldest file in the entire set as a fallback.
            // That way it can eventually become a day/week/month-old snapshot.
            string oldestFile = backupFiles[backupFiles.Length - 1];  // last in array = oldest
            if (!foundDay || !foundWeek || !foundMonth)
            {
                keepers.Add(oldestFile);
            }

            // Now delete anything that's not a keeper
            foreach (var file in backupFiles)
            {
                if (!keepers.Contains(file))
                {
                    try
                    {
                        File.Delete(file);
                        sapi.Logger.Notification($"[Auto Server Management] Deleted old backup: {Path.GetFileName(file)}");
                    }
                    catch (Exception ex)
                    {
                        sapi.Logger.Warning($"[Auto Server Management] Could not delete {file}. Reason: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Sends a notification message to a specific player (if provided) 
        /// or logs a notification to the server console/log if player is null.
        /// </summary>
        private void MessageCaller(IServerPlayer player, string message)
        {
            if (player != null)
            {
                player.SendMessage(GlobalConstants.InfoLogChatGroup, message, EnumChatType.Notification);
            }
            else
            {
                sapi.Logger.Notification("[Auto Server Management] " + message);
            }
        }
    }
}