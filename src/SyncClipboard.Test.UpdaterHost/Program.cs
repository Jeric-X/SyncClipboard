// A disposable application used to verify the real updater's restart without launching SyncClipboard.
File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "restart-marker.txt"), "restarted");
