import AppKit

/// One display in "CG space": points, origin at the top-left of the primary display, y grows down.
/// This is the space CGWindowListCopyWindowInfo reports window bounds in.
struct ScreenArea {
    let frame: CGRect
    /// Bottom of the menu bar (where the garland hangs).
    let menuBarBottom: CGFloat
    /// Bottom of the usable area (above the Dock when it sits at the bottom).
    let visibleBottom: CGFloat
    var target = 0

    static var primaryHeight: CGFloat { NSScreen.screens.first?.frame.height ?? 0 }

    init(_ screen: NSScreen) {
        let h = ScreenArea.primaryHeight
        let f = screen.frame, v = screen.visibleFrame
        frame = CGRect(x: f.minX, y: h - f.maxY, width: f.width, height: f.height)
        let menuBar = f.maxY - v.maxY
        menuBarBottom = frame.minY + (menuBar > 0 ? menuBar : 24)
        visibleBottom = h - v.minY
    }

    /// AppKit point (origin bottom-left of primary) to CG space.
    static func toCG(_ p: NSPoint) -> CGPoint { CGPoint(x: p.x, y: primaryHeight - p.y) }
}
