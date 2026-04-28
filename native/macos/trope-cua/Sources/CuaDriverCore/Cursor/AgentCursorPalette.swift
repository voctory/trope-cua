import AppKit
import Darwin
import Foundation

public struct AgentCursorPalette: @unchecked Sendable {
    public let name: String
    public let cursorStart: NSColor
    public let cursorMid: NSColor
    public let cursorEnd: NSColor
    public let bloomOuter: NSColor
    public let bloomInner: NSColor

    public init(
        name: String,
        cursorStart: NSColor,
        cursorMid: NSColor,
        cursorEnd: NSColor,
        bloomOuter: NSColor,
        bloomInner: NSColor
    ) {
        self.name = name
        self.cursorStart = cursorStart
        self.cursorMid = cursorMid
        self.cursorEnd = cursorEnd
        self.bloomOuter = bloomOuter
        self.bloomInner = bloomInner
    }

    public static func forSlot(_ slot: Int) -> AgentCursorPalette {
        if slot <= 0 { return defaultBlue }
        return alternates[(slot - 1) % alternates.count]
    }

    public static func slot(named name: String) -> Int? {
        if name == defaultBlue.name { return 0 }
        guard let index = alternates.firstIndex(where: { $0.name == name }) else {
            return nil
        }
        return index + 1
    }

    public static let defaultBlue = AgentCursorPalette(
        name: "default_blue",
        cursorStart: color(0xDB, 0xEE, 0xFF),
        cursorMid: color(0x5E, 0xC0, 0xE8),
        cursorEnd: color(0x54, 0xCD, 0xA0),
        bloomOuter: color(0xBC, 0xE8, 0xFC),
        bloomInner: color(0xEE, 0xF8, 0xFF)
    )

    public static let alternates: [AgentCursorPalette] = [
        AgentCursorPalette(
            name: "soft_purple",
            cursorStart: color(0xEE, 0xE2, 0xFF),
            cursorMid: color(0xB2, 0x84, 0xFF),
            cursorEnd: color(0x76, 0xC2, 0xFF),
            bloomOuter: color(0xD6, 0xBC, 0xFF),
            bloomInner: color(0xF6, 0xEE, 0xFF)
        ),
        AgentCursorPalette(
            name: "rose_gold",
            cursorStart: color(0xFF, 0xE7, 0xEE),
            cursorMid: color(0xF7, 0x84, 0xAA),
            cursorEnd: color(0xFF, 0xB5, 0x6C),
            bloomOuter: color(0xFF, 0xBE, 0xD3),
            bloomInner: color(0xFF, 0xF3, 0xE8)
        ),
        AgentCursorPalette(
            name: "mint_lime",
            cursorStart: color(0xE2, 0xFF, 0xF0),
            cursorMid: color(0x60, 0xDA, 0xAE),
            cursorEnd: color(0xB2, 0xE5, 0x48),
            bloomOuter: color(0xB2, 0xF5, 0xD9),
            bloomInner: color(0xF1, 0xFF, 0xE7)
        ),
        AgentCursorPalette(
            name: "amber",
            cursorStart: color(0xFF, 0xF4, 0xD6),
            cursorMid: color(0xF4, 0xB2, 0x42),
            cursorEnd: color(0xFF, 0x7E, 0x5C),
            bloomOuter: color(0xFF, 0xDB, 0x8C),
            bloomInner: color(0xFF, 0xF8, 0xE1)
        ),
        AgentCursorPalette(
            name: "aqua",
            cursorStart: color(0xDD, 0xFC, 0xFF),
            cursorMid: color(0x4C, 0xCC, 0xE0),
            cursorEnd: color(0x3F, 0xDE, 0xA6),
            bloomOuter: color(0xAC, 0xF1, 0xF9),
            bloomInner: color(0xEC, 0xFF, 0xFB)
        ),
        AgentCursorPalette(
            name: "orchid",
            cursorStart: color(0xFC, 0xE4, 0xFF),
            cursorMid: color(0xDD, 0x71, 0xEC),
            cursorEnd: color(0xFF, 0x8B, 0xC4),
            bloomOuter: color(0xED, 0xB5, 0xF6),
            bloomInner: color(0xFF, 0xEF, 0xFC)
        ),
        AgentCursorPalette(
            name: "crimson",
            cursorStart: color(0xFF, 0xE2, 0xE2),
            cursorMid: color(0xE8, 0x52, 0x62),
            cursorEnd: color(0x96, 0x5E, 0xFF),
            bloomOuter: color(0xFF, 0xA8, 0xB2),
            bloomInner: color(0xFF, 0xF0, 0xF1)
        ),
        AgentCursorPalette(
            name: "chartreuse",
            cursorStart: color(0xF7, 0xFF, 0xDA),
            cursorMid: color(0xB8, 0xDC, 0x36),
            cursorEnd: color(0x48, 0xBE, 0x77),
            bloomOuter: color(0xE0, 0xF7, 0x80),
            bloomInner: color(0xF9, 0xFF, 0xE8)
        ),
        AgentCursorPalette(
            name: "cobalt",
            cursorStart: color(0xE2, 0xEB, 0xFF),
            cursorMid: color(0x50, 0x7E, 0xEC),
            cursorEnd: color(0x5B, 0xDB, 0xDE),
            bloomOuter: color(0xAA, 0xC3, 0xFF),
            bloomInner: color(0xEF, 0xF6, 0xFF)
        ),
    ]

    private static func color(_ r: Int, _ g: Int, _ b: Int) -> NSColor {
        NSColor(
            red: CGFloat(r) / 255.0,
            green: CGFloat(g) / 255.0,
            blue: CGFloat(b) / 255.0,
            alpha: 1
        )
    }
}

public enum AgentCursorPaletteRegistry {
    public static func claimSlot(context: String) -> Int {
        if let forced = ProcessInfo.processInfo.environment["TROPE_CUA_CURSOR_PALETTE"],
           let slot = AgentCursorPalette.slot(named: forced)
        {
            return slot
        }

        let pid = Int(ProcessInfo.processInfo.processIdentifier)
        let now = Date().timeIntervalSince1970
        let cacheDir = FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent("Library/Caches/trope-cua", isDirectory: true)
        let registryURL = cacheDir.appendingPathComponent("cursor-palettes.tsv")
        let lockURL = cacheDir.appendingPathComponent("cursor-palettes.lock")

        do {
            try FileManager.default.createDirectory(
                at: cacheDir, withIntermediateDirectories: true)
        } catch {
            return fallbackSlot(context: context)
        }

        let fd = open(lockURL.path, O_RDWR | O_CREAT, 0o600)
        guard fd >= 0 else { return fallbackSlot(context: context) }
        defer { close(fd) }

        guard flock(fd, LOCK_EX) == 0 else { return fallbackSlot(context: context) }
        defer { flock(fd, LOCK_UN) }

        var records = loadRecords(from: registryURL)
            .filter { record in
                record.pid == pid || (isProcessAlive(record.pid) && now - record.updatedAt < 86_400)
            }
        records.removeAll { $0.pid == pid }
        records.append(
            CursorPaletteRecord(
                pid: pid,
                claimedAt: now,
                updatedAt: now,
                context: sanitizedContext(context)
            )
        )
        records.sort {
            if $0.claimedAt == $1.claimedAt { return $0.pid < $1.pid }
            return $0.claimedAt < $1.claimedAt
        }
        saveRecords(records, to: registryURL)
        return records.firstIndex { $0.pid == pid } ?? fallbackSlot(context: context)
    }

    private static func fallbackSlot(context: String) -> Int {
        var hash = UInt32(2_166_136_261)
        for byte in context.utf8 {
            hash ^= UInt32(byte)
            hash &*= 16_777_619
        }
        return Int(hash % UInt32(AgentCursorPalette.alternates.count + 1))
    }

    private static func loadRecords(from url: URL) -> [CursorPaletteRecord] {
        guard let data = try? Data(contentsOf: url),
              let text = String(data: data, encoding: .utf8)
        else { return [] }

        return text.split(separator: "\n").compactMap { line in
            let parts = line.split(separator: "\t", maxSplits: 3, omittingEmptySubsequences: false)
            guard parts.count == 4,
                  let pid = Int(parts[0]),
                  let claimedAt = TimeInterval(String(parts[1])),
                  let updatedAt = TimeInterval(String(parts[2]))
            else { return nil }

            return CursorPaletteRecord(
                pid: pid,
                claimedAt: claimedAt,
                updatedAt: updatedAt,
                context: String(parts[3])
            )
        }
    }

    private static func saveRecords(_ records: [CursorPaletteRecord], to url: URL) {
        let text = records.map {
            "\($0.pid)\t\($0.claimedAt)\t\($0.updatedAt)\t\($0.context)"
        }.joined(separator: "\n") + "\n"
        try? Data(text.utf8).write(to: url, options: .atomic)
    }

    private static func isProcessAlive(_ pid: Int) -> Bool {
        if pid <= 0 { return false }
        if kill(pid_t(pid), 0) == 0 { return true }
        return errno == EPERM
    }

    private static func sanitizedContext(_ context: String) -> String {
        context
            .replacingOccurrences(of: "\t", with: " ")
            .replacingOccurrences(of: "\n", with: " ")
    }
}

private struct CursorPaletteRecord {
    let pid: Int
    let claimedAt: TimeInterval
    let updatedAt: TimeInterval
    let context: String
}
