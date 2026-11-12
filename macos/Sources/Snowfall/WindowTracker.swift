import AppKit

/// A window whose top edge can hold snow.
struct TrackedWindow {
    let id: Int
    let rect: CGRect
    /// Parts of the top edge not covered by windows above it, as [left, right).
    var exposed: [(CGFloat, CGFloat)]
}

/// Lists on-screen app windows front to back and computes their exposed top edges.
/// Bounds, layer and owner PID are available without the Screen Recording permission.
final class WindowTracker {
    private let ownPID = ProcessInfo.processInfo.processIdentifier
    private(set) var windows: [TrackedWindow] = []

    func refresh(screens: [ScreenArea]) {
        guard let list = CGWindowListCopyWindowInfo([.optionOnScreenOnly, .excludeDesktopElements], kCGNullWindowID)
                as? [[String: Any]] else { return }

        var raw: [(Int, CGRect)] = []
        for info in list {
            guard let layer = info[kCGWindowLayer as String] as? Int, layer == 0,
                  let pid = info[kCGWindowOwnerPID as String] as? Int32, pid != ownPID,
                  let number = info[kCGWindowNumber as String] as? Int,
                  let boundsDict = info[kCGWindowBounds as String] as? NSDictionary,
                  let rect = CGRect(dictionaryRepresentation: boundsDict as CFDictionary)
            else { continue }
            let alpha = info[kCGWindowAlpha as String] as? Double ?? 1
            if alpha < 0.1 || rect.width < 120 || rect.height < 40 { continue }
            raw.append((number, rect))
        }

        var result: [TrackedWindow] = []
        for (i, (number, rect)) in raw.enumerated() {
            var exposed: [(CGFloat, CGFloat)] = []
            // Windows pressed against the menu bar (zoomed, full screen) hold no snow: every flake would land there.
            if let screen = screens.first(where: { $0.frame.intersects(rect) }), rect.minY > screen.menuBarBottom + 4 {
                exposed = Self.exposedEdge(rect.minX, rect.maxX, rect.minY, above: raw[0..<i].map { $0.1 })
            }
            result.append(TrackedWindow(id: number, rect: rect, exposed: exposed))
        }
        windows = result
    }

    private static func exposedEdge(_ left: CGFloat, _ right: CGFloat, _ y: CGFloat, above: [CGRect]) -> [(CGFloat, CGFloat)] {
        let tolerance: CGFloat = 3
        var segs: [(CGFloat, CGFloat)] = [(left, right)]
        for r in above where r.minY < y - tolerance && r.maxY > y + tolerance {
            var next: [(CGFloat, CGFloat)] = []
            for (l, rr) in segs {
                let cl = r.minX + tolerance, cr = r.maxX - tolerance
                if cr <= l || cl >= rr { next.append((l, rr)); continue }
                if l < cl { next.append((l, cl)) }
                if cr < rr { next.append((cr, rr)) }
            }
            segs = next
            if segs.isEmpty { break }
        }
        return segs.filter { $0.1 - $0.0 >= 6 }
    }
}
