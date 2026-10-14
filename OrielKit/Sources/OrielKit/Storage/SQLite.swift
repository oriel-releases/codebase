import Foundation
import SQLite3

/// A small wrapper over the system SQLite library: enough for the history table, with no third-party dependency.
/// Not thread-safe by itself; SQLiteHistoryStore serializes access.
final class SQLiteDatabase {
    enum Value {
        case text(String)
        case int(Int64)
        case null

        static func optional(_ s: String?) -> Value { s.map(Value.text) ?? .null }
    }

    struct Error: Swift.Error, CustomStringConvertible {
        let description: String
    }

    private var handle: OpaquePointer?

    init(path: String) throws {
        let flags = SQLITE_OPEN_READWRITE | SQLITE_OPEN_CREATE | SQLITE_OPEN_FULLMUTEX
        guard sqlite3_open_v2(path, &handle, flags, nil) == SQLITE_OK else {
            let message = handle.map { String(cString: sqlite3_errmsg($0)) } ?? "out of memory"
            sqlite3_close(handle)
            throw Error(description: "Couldn't open \(path): \(message)")
        }
        // The app and a command-line run may both use the database.
        sqlite3_busy_timeout(handle, 5000)
    }

    deinit {
        sqlite3_close(handle)
    }

    /// Runs one or more statements that return nothing.
    func execute(_ sql: String) throws {
        var error: UnsafeMutablePointer<CChar>?
        guard sqlite3_exec(handle, sql, nil, nil, &error) == SQLITE_OK else {
            let message = error.map { String(cString: $0) } ?? "unknown error"
            sqlite3_free(error)
            throw Error(description: message)
        }
    }

    func run(_ sql: String, _ args: [Value] = []) throws {
        try withStatement(sql, args) { statement in
            let rc = sqlite3_step(statement)
            guard rc == SQLITE_DONE || rc == SQLITE_ROW else { throw lastError() }
        }
    }

    func query<T>(_ sql: String, _ args: [Value] = [], row: (Row) throws -> T) throws -> [T] {
        try withStatement(sql, args) { statement in
            var results: [T] = []
            while true {
                let rc = sqlite3_step(statement)
                if rc == SQLITE_DONE { return results }
                guard rc == SQLITE_ROW else { throw lastError() }
                results.append(try row(Row(statement: statement)))
            }
        }
    }

    /// Runs `body` in a transaction: all of it or none.
    func transaction(_ body: () throws -> Void) throws {
        try execute("BEGIN IMMEDIATE")
        do {
            try body()
            try execute("COMMIT")
        } catch {
            try? execute("ROLLBACK")
            throw error
        }
    }

    struct Row {
        let statement: OpaquePointer

        private func index(_ column: String) -> Int32 {
            for i in 0..<sqlite3_column_count(statement) where String(cString: sqlite3_column_name(statement, i)) == column {
                return i
            }
            preconditionFailure("No column \(column)")
        }

        func string(_ column: String) -> String {
            optionalString(column) ?? ""
        }

        func optionalString(_ column: String) -> String? {
            let i = index(column)
            guard sqlite3_column_type(statement, i) != SQLITE_NULL, let text = sqlite3_column_text(statement, i) else { return nil }
            return String(cString: text)
        }

        func int(_ column: String) -> Int64 {
            sqlite3_column_int64(statement, index(column))
        }
    }

    private func withStatement<T>(_ sql: String, _ args: [Value], _ body: (OpaquePointer) throws -> T) throws -> T {
        var statement: OpaquePointer?
        guard sqlite3_prepare_v2(handle, sql, -1, &statement, nil) == SQLITE_OK, let statement else { throw lastError() }
        defer { sqlite3_finalize(statement) }
        for (i, arg) in args.enumerated() {
            let position = Int32(i + 1)
            switch arg {
            case .text(let s): sqlite3_bind_text(statement, position, s, -1, Self.transient)
            case .int(let n): sqlite3_bind_int64(statement, position, n)
            case .null: sqlite3_bind_null(statement, position)
            }
        }
        return try body(statement)
    }

    private func lastError() -> Error {
        Error(description: String(cString: sqlite3_errmsg(handle)))
    }

    /// Tells SQLite to copy bound strings (Swift's are temporary).
    private static let transient = unsafeBitCast(-1, to: sqlite3_destructor_type.self)
}
