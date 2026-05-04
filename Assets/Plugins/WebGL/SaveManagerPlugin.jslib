mergeInto(LibraryManager.library, {
    SyncFilesystem: function() {
        FS.syncfs(false, function(err) {
            if (err) console.error("[SaveManager] FS.syncfs error: " + err);
            else console.log("[SaveManager] Filesystem synced to IndexedDB.");
        });
    }
});