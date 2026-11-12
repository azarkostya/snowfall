import AppKit

/// Borderless, transparent, click-through window over one display, above the menu bar and on every Space.
final class OverlayWindow: NSWindow {
    init(screen: NSScreen, view: SnowView) {
        super.init(contentRect: screen.frame, styleMask: .borderless, backing: .buffered, defer: false)
        isOpaque = false
        backgroundColor = .clear
        hasShadow = false
        ignoresMouseEvents = true
        level = NSWindow.Level(rawValue: Int(CGWindowLevelForKey(.overlayWindow)))
        collectionBehavior = [.canJoinAllSpaces, .stationary, .ignoresCycle, .fullScreenAuxiliary]
        isReleasedWhenClosed = false
        contentView = view
        setFrame(screen.frame, display: false)
    }

    override var canBecomeKey: Bool { false }
    override var canBecomeMain: Bool { false }
}

/// Draws piles, decorations and flakes for one screen. Flipped, so drawing happens in CG space (y down).
final class SnowView: NSView {
    var area: ScreenArea
    weak var model: SnowModel?

    init(area: ScreenArea, model: SnowModel) {
        self.area = area
        self.model = model
        super.init(frame: NSRect(origin: .zero, size: area.frame.size))
        wantsLayer = true
        layer?.isOpaque = false
    }

    required init?(coder: NSCoder) { fatalError("not used") }

    override var isFlipped: Bool { true }

    override func draw(_ dirtyRect: NSRect) {
        guard let ctx = NSGraphicsContext.current?.cgContext else { return }
        ctx.clear(bounds)
        guard let model, model.settings.enabled else { return }

        ctx.saveGState()
        ctx.translateBy(x: -area.frame.minX, y: -area.frame.minY)

        if model.settings.piles {
            for p in model.sim.piles.values { drawPile(ctx, p) }
        }
        model.decorations.draw(ctx, settings: model.settings, screen: area, t: model.time)

        let f = area.frame
        let shadow = CGColor(red: 70 / 255, green: 90 / 255, blue: 120 / 255, alpha: 0.18)
        for flake in model.sim.flakes where !flake.dead {
            let r = flake.r
            if flake.x < f.minX - r || flake.x > f.maxX + r || flake.y < f.minY - r || flake.y > f.maxY + r { continue }
            if r > 1.5 {
                ctx.setFillColor(shadow)
                ctx.fillEllipse(in: CGRect(x: flake.x - r - 0.8, y: flake.y - r - 0.3, width: 2 * r + 1.6, height: 2 * r + 1.6))
            }
            ctx.setFillColor(CGColor(red: 1, green: 1, blue: 1, alpha: flake.alpha))
            ctx.fillEllipse(in: CGRect(x: flake.x - r, y: flake.y - r, width: 2 * r, height: 2 * r))
        }
        ctx.restoreGState()
    }

    private func drawPile(_ ctx: CGContext, _ p: Pile) {
        let f = area.frame
        let left = p.rect.minX
        if p.rect.maxX < f.minX || left > f.maxX || p.rect.minY < f.minY || p.rect.minY - 20 > f.maxY { return }

        let fill = CGColor(red: 250 / 255, green: 252 / 255, blue: 1, alpha: 0.97)
        let edge = CGColor(red: 150 / 255, green: 172 / 255, blue: 205 / 255, alpha: 0.47)
        // Sink the base slightly into the window so no gap shows above its edge.
        let baseY = p.rect.minY + 1.5

        for (l, r) in p.exposed {
            let a = Int(max(l, f.minX) - left), b = Int(min(r, f.maxX) - left)
            if b - a < 2 || a < 0 || b > p.h.count { continue }
            var peak: CGFloat = 0
            for i in a..<b { peak = max(peak, p.h[i]) }
            if peak < 0.6 { continue }

            let path = CGMutablePath()
            path.move(to: CGPoint(x: left + CGFloat(a), y: baseY))
            var i = a
            while i < b { path.addLine(to: CGPoint(x: left + CGFloat(i), y: baseY - p.h[i])); i += 2 }
            path.addLine(to: CGPoint(x: left + CGFloat(b - 1), y: baseY - p.h[b - 1]))
            path.addLine(to: CGPoint(x: left + CGFloat(b - 1), y: baseY))
            path.closeSubpath()

            ctx.addPath(path)
            ctx.setFillColor(fill)
            ctx.fillPath()
            ctx.addPath(path)
            ctx.setStrokeColor(edge)
            ctx.setLineWidth(1)
            ctx.strokePath()
        }
    }
}
