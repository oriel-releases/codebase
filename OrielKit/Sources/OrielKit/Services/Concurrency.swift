import Foundation
import Synchronization

/// An event several parts of the app listen to (C# `event`): handlers are called on whatever thread raises it.
public final class Broadcaster<Value: Sendable>: Sendable {
    public typealias Token = UUID

    private let handlers = Mutex<[UUID: @Sendable (Value) -> Void]>([:])

    public init() {}

    @discardableResult
    public func subscribe(_ handler: @escaping @Sendable (Value) -> Void) -> Token {
        let token = UUID()
        handlers.withLock { $0[token] = handler }
        return token
    }

    public func unsubscribe(_ token: Token) {
        _ = handlers.withLock { $0.removeValue(forKey: token) }
    }

    public func send(_ value: Value) {
        for handler in handlers.withLock({ Array($0.values) }) { handler(value) }
    }
}

/// One at a time, for async work (C# `SemaphoreSlim(1, 1)`). Waiters go in order.
public final class AsyncGate: Sendable {
    private let state = Mutex<(busy: Bool, waiters: [CheckedContinuation<Void, Never>])>((false, []))

    public init() {}

    public func lock() async {
        await withCheckedContinuation { (c: CheckedContinuation<Void, Never>) in
            let now = state.withLock { s -> Bool in
                if s.busy {
                    s.waiters.append(c)
                    return false
                }
                s.busy = true
                return true
            }
            if now { c.resume() }
        }
    }

    /// Takes the gate only if it's free right now (C# `WaitAsync(0)`).
    public func tryLock() -> Bool {
        state.withLock { s in
            if s.busy { return false }
            s.busy = true
            return true
        }
    }

    public func unlock() {
        let next = state.withLock { s -> CheckedContinuation<Void, Never>? in
            if s.waiters.isEmpty {
                s.busy = false
                return nil
            }
            return s.waiters.removeFirst()
        }
        next?.resume()
    }
}

/// Runs async jobs one after another, in the order they were added, without the caller waiting.
public final class SerialQueue: Sendable {
    private let tail = Mutex<Task<Void, Never>?>(nil)

    public init() {}

    public func add(_ work: @escaping @Sendable () async -> Void) {
        tail.withLock { last in
            let previous = last
            last = Task {
                await previous?.value
                await work()
            }
        }
    }

    /// Done once everything added so far has finished.
    public func drain() async {
        await tail.withLock { $0 }?.value
    }
}

/// Fails with `TimeoutError` when `body` takes longer than `seconds` (and cancels it).
public func withTimeout<T: Sendable>(_ seconds: Double, _ body: @escaping @Sendable () async throws -> T) async throws -> T {
    try await withThrowingTaskGroup(of: T.self) { group in
        group.addTask { try await body() }
        group.addTask {
            try await Task.sleep(for: .seconds(seconds))
            throw TimeoutError()
        }
        defer { group.cancelAll() }
        return try await group.next()!
    }
}

public struct TimeoutError: Error, LocalizedError {
    public var errorDescription: String? { "It took too long." }
}

/// A number several tasks change at once.
public final class Counter: Sendable {
    private let count = Mutex(0)

    public init() {}

    public var value: Int { count.withLock { $0 } }

    /// Adds `n` and returns the new value.
    @discardableResult
    public func add(_ n: Int) -> Int {
        count.withLock { c in
            c += n
            return c
        }
    }
}
