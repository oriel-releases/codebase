import Foundation

/// An exclusive lock shared between processes (the app and a command-line run), held while photos are being
/// saved or moved. Released when this object goes away or the process ends.
public final class ApplyLock {
    private let descriptor: Int32

    private init(descriptor: Int32) {
        self.descriptor = descriptor
    }

    /// The lock, or nil when someone else holds it right now.
    public static func tryAcquire(_ file: URL) -> ApplyLock? {
        try? FileManager.default.createDirectory(at: file.deletingLastPathComponent(), withIntermediateDirectories: true)
        let fd = open(file.path(percentEncoded: false), O_RDWR | O_CREAT | O_CLOEXEC, 0o644)
        guard fd >= 0 else { return nil }
        guard flock(fd, LOCK_EX | LOCK_NB) == 0 else {
            close(fd)
            return nil
        }
        return ApplyLock(descriptor: fd)
    }

    deinit {
        flock(descriptor, LOCK_UN)
        close(descriptor)
    }
}
