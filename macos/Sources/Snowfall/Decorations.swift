import CoreGraphics
import Foundation

private func rgb(_ r: CGFloat, _ g: CGFloat, _ b: CGFloat, _ a: CGFloat = 1) -> CGColor {
    CGColor(red: r / 255, green: g / 255, blue: b / 255, alpha: a)
}

/// Christmas lights under the menu bar, a snowman and a tree in the bottom-left corner. All vector, no assets.
/// Draws into a flipped context (y down) in CG-space points.
final class Decorations {
    private static let palette: [(CGFloat, CGFloat, CGFloat)] = [
        (255, 70, 70), (255, 200, 60), (70, 225, 110), (80, 160, 255), (255, 100, 210),
    ]
    private let glowLevels = 5
    private var glow: [CGGradient] = [] // index = color * glowLevels + level

    private let colorSpace = CGColorSpaceCreateDeviceRGB()
    private let wire = rgb(30, 48, 36, 0.86)
    private let cap = rgb(40, 62, 42)
    private let snow = rgb(250, 252, 255, 0.99)
    private let snowEdge = rgb(150, 168, 198, 0.67)
    private let coal = rgb(30, 30, 36)
    private let carrot = rgb(255, 140, 30)
    private let scarf = rgb(210, 40, 50)
    private let branch = rgb(110, 70, 40)
    private let needles = rgb(30, 122, 66)
    private let needlesEdge = rgb(18, 82, 44)
    private let trunk = rgb(105, 66, 38)
    private let star = rgb(255, 214, 70)

    init() {
        for (r, g, b) in Self.palette {
            for level in 0..<glowLevels {
                let a = (25 + 110 * CGFloat(level + 1) / CGFloat(glowLevels)) / 255
                let colors = [rgb(r, g, b, a), rgb(r, g, b, 0)] as CFArray
                glow.append(CGGradient(colorsSpace: colorSpace, colors: colors, locations: [0, 1])!)
            }
        }
    }

    func draw(_ ctx: CGContext, settings: Settings, screen: ScreenArea, t: CGFloat) {
        let f = screen.frame
        if settings.snowman { drawSnowman(ctx, x: f.minX + 34, floor: screen.visibleBottom - 4, t: t) }
        if settings.tree { drawTree(ctx, x: f.minX + (settings.snowman ? 84 : 34), floor: screen.visibleBottom - 4, t: t) }
        if settings.garland { drawGarland(ctx, left: f.minX, right: f.maxX, top: screen.menuBarBottom, t: t, mode: settings.garlandMode) }
    }

    // MARK: - garland

    private func drawGarland(_ ctx: CGContext, left: CGFloat, right: CGFloat, top: CGFloat, t: CGFloat, mode: GarlandMode) {
        let span: CGFloat = 120, sag: CGFloat = 5, y0 = top + 1
        func wireY(_ x: CGFloat) -> CGFloat {
            let u = (x - left).truncatingRemainder(dividingBy: span) / span
            return y0 + sag * 4 * u * (1 - u)
        }

        ctx.setStrokeColor(wire)
        ctx.setLineWidth(1.3)
        ctx.beginPath()
        ctx.move(to: CGPoint(x: left, y: wireY(left)))
        var x = left
        while x <= right { ctx.addLine(to: CGPoint(x: x, y: wireY(x))); x += 6 }
        ctx.strokePath()

        let spacing: CGFloat = 24
        var i = 0
        x = left + spacing / 2
        while x < right {
            let c = i % Self.palette.count
            let b = Self.brightness(i, t, mode)
            let wy = wireY(x)
            let bw: CGFloat = 4.6, bh: CGFloat = 6.6
            let cy = wy + 1.6 + bh / 2
            drawGlow(ctx, color: c, brightness: b, center: CGPoint(x: x, y: cy), radius: 7)
            ctx.setFillColor(cap)
            ctx.fill(CGRect(x: x - 1.5, y: wy - 0.4, width: 3, height: 2.4))
            ctx.setFillColor(bulbColor(c, b))
            ctx.fillEllipse(in: CGRect(x: x - bw / 2, y: wy + 1.6, width: bw, height: bh))
            ctx.setFillColor(rgb(255, 255, 255, (60 + 150 * b) / 255))
            ctx.fillEllipse(in: CGRect(x: x - bw / 2 + 0.9, y: wy + 2.6, width: 1.4, height: 2))
            x += spacing
            i += 1
        }
    }

    private static func brightness(_ i: Int, _ t: CGFloat, _ mode: GarlandMode) -> CGFloat {
        let h = UInt32(truncatingIfNeeded: i) &* 2654435761
        let phase = CGFloat(h & 0xFFFF) / 65535 * 2 * .pi
        let speed = 0.7 + CGFloat((h >> 16) & 0xFF) / 255 * 0.9
        switch mode {
        case .chase:
            let pos = t * 6 - CGFloat(i)
            let k = pos - 3 * floor(pos / 3)
            return k < 1 ? 1 - 0.6 * k : 0.25
        case .steady:
            return 0.85 + 0.15 * sin(t * 0.8 + CGFloat(i) * 0.35)
        case .twinkle:
            let w = 0.5 + 0.5 * sin(t * 1.8 * speed + phase)
            return 0.25 + 0.75 * w * w
        }
    }

    private func bulbColor(_ c: Int, _ b: CGFloat) -> CGColor {
        let (r, g, bl) = Self.palette[c]
        let k = min(max(b, 0), 1)
        let dim: CGFloat = 0.4
        return rgb(r * (dim + (1 - dim) * k), g * (dim + (1 - dim) * k), bl * (dim + (1 - dim) * k))
    }

    private func drawGlow(_ ctx: CGContext, color: Int, brightness: CGFloat, center: CGPoint, radius: CGFloat) {
        let level = min(max(Int(brightness * CGFloat(glowLevels)), 0), glowLevels - 1)
        ctx.drawRadialGradient(glow[color * glowLevels + level], startCenter: center, startRadius: 0,
                               endCenter: center, endRadius: radius, options: [])
    }

    // MARK: - snowman

    private func drawSnowman(_ ctx: CGContext, x: CGFloat, floor: CGFloat, t: CGFloat) {
        ctx.saveGState()
        ctx.translateBy(x: x, y: floor)

        // Arms behind the body. The right one waves for ~2 s every 9 s.
        let cycle = t.truncatingRemainder(dividingBy: 9)
        let wave: CGFloat = cycle < 2 ? 0.45 * sin(cycle * 11) * sin(cycle / 2 * .pi) : 0
        ctx.setStrokeColor(branch)
        ctx.setLineWidth(1.6)
        ctx.setLineCap(.round)
        line(ctx, -8, -30, -20, -39)
        line(ctx, -17, -36.5, -19, -42)
        let a = -0.6 + wave
        let ex = 8 + 13 * cos(a), ey = -30 + 13 * sin(a)
        line(ctx, 8, -30, ex, ey)
        line(ctx, 8 + 9 * cos(a), -30 + 9 * sin(a), ex + 2, ey - 4)

        ctx.setLineWidth(1)
        for (cy, r) in [(CGFloat(-12), CGFloat(12)), (-29, 9), (-43, 7)] {
            let rect = CGRect(x: -r, y: cy - r, width: 2 * r, height: 2 * r)
            ctx.setFillColor(snow)
            ctx.fillEllipse(in: rect)
            ctx.setStrokeColor(snowEdge)
            ctx.strokeEllipse(in: rect)
        }

        ctx.setFillColor(coal)
        ctx.fillEllipse(in: CGRect(x: -3.6, y: -46.2, width: 2.2, height: 2.2))
        ctx.fillEllipse(in: CGRect(x: 1.4, y: -46.2, width: 2.2, height: 2.2))
        for (mx, my) in [(CGFloat(-3), CGFloat(-39.6)), (-1.5, -38.9), (0, -38.7), (1.5, -38.9), (3, -39.6)] {
            ctx.fillEllipse(in: CGRect(x: mx - 0.6, y: my - 0.6, width: 1.2, height: 1.2))
        }
        for by in [CGFloat(-32), -27.5, -16, -10] {
            ctx.fillEllipse(in: CGRect(x: -1.2, y: by - 1.2, width: 2.4, height: 2.4))
        }
        polygon(ctx, carrot, [(0, -43.6), (8, -42.6), (0, -41.4)])

        ctx.setFillColor(scarf)
        ctx.fill(CGRect(x: -7.2, y: -37.4, width: 14.4, height: 3.4))
        polygon(ctx, scarf, [(2.5, -35), (6.5, -35), (8, -27), (4, -27.6)])

        // Hat, outlined so it doesn't vanish against dark windows.
        for rect in [CGRect(x: -6, y: -62, width: 12, height: 12), CGRect(x: -9, y: -50.5, width: 18, height: 2.6)] {
            ctx.setFillColor(coal)
            ctx.fill(rect)
            ctx.setStrokeColor(snowEdge)
            ctx.stroke(rect)
        }
        ctx.setFillColor(scarf)
        ctx.fill(CGRect(x: -6, y: -52.8, width: 12, height: 2))
        ctx.setFillColor(snow)
        ctx.fillEllipse(in: CGRect(x: -6.5, y: -63.5, width: 13, height: 3))

        ctx.restoreGState()
    }

    // MARK: - tree

    private static let ornaments: [(CGFloat, CGFloat)] = [
        (-12, -13), (-3, -16), (8, -13), (15, -10.5), (-9, -28), (3, -30), (10, -26.5), (-5, -40), (5, -42), (0, -50),
    ]

    private func drawTree(_ ctx: CGContext, x: CGFloat, floor: CGFloat, t: CGFloat) {
        ctx.saveGState()
        ctx.translateBy(x: x, y: floor)

        ctx.setFillColor(trunk)
        ctx.fill(CGRect(x: -3, y: -9, width: 6, height: 9))
        for tier in [[(CGFloat(0), CGFloat(-34)), (-21, -8), (21, -8)],
                     [(0, -46), (-16, -23), (16, -23)],
                     [(0, -57), (-11, -35), (11, -35)]] {
            polygon(ctx, needles, tier, stroke: needlesEdge)
        }
        ctx.setFillColor(snow)
        for (dx, dy, w, h) in [(CGFloat(-16), CGFloat(-10), CGFloat(9), CGFloat(2.6)), (6, -11, 10, 2.6), (-12, -25, 8, 2.4),
                               (4, -25.5, 9, 2.4), (-7, -37, 6, 2.2), (2, -37.5, 6, 2.2)] {
            ctx.fillEllipse(in: CGRect(x: dx, y: dy, width: w, height: h))
        }

        for (i, o) in Self.ornaments.enumerated() {
            let c = (i * 3) % Self.palette.count
            let b = Self.brightness(i + 7, t, .twinkle)
            let p = CGPoint(x: o.0, y: o.1)
            drawGlow(ctx, color: c, brightness: b, center: p, radius: 6)
            ctx.setFillColor(bulbColor(c, b))
            ctx.fillEllipse(in: CGRect(x: p.x - 1.9, y: p.y - 1.9, width: 3.8, height: 3.8))
        }

        let sp = CGPoint(x: 0, y: -58)
        drawGlow(ctx, color: 1, brightness: 0.75 + 0.25 * sin(t * 2.3), center: sp, radius: 13)
        var pts: [(CGFloat, CGFloat)] = []
        for i in 0..<10 {
            let r: CGFloat = i % 2 == 0 ? 5.5 : 2.3
            let a = -CGFloat.pi / 2 + CGFloat(i) * .pi / 5
            pts.append((sp.x + r * cos(a), sp.y + r * sin(a)))
        }
        polygon(ctx, star, pts)

        ctx.restoreGState()
    }

    // MARK: - helpers

    private func line(_ ctx: CGContext, _ x1: CGFloat, _ y1: CGFloat, _ x2: CGFloat, _ y2: CGFloat) {
        ctx.beginPath()
        ctx.move(to: CGPoint(x: x1, y: y1))
        ctx.addLine(to: CGPoint(x: x2, y: y2))
        ctx.strokePath()
    }

    private func polygon(_ ctx: CGContext, _ fill: CGColor, _ pts: [(CGFloat, CGFloat)], stroke: CGColor? = nil) {
        guard let first = pts.first else { return }
        ctx.beginPath()
        ctx.move(to: CGPoint(x: first.0, y: first.1))
        for p in pts.dropFirst() { ctx.addLine(to: CGPoint(x: p.0, y: p.1)) }
        ctx.closePath()
        ctx.setFillColor(fill)
        if let stroke {
            ctx.setStrokeColor(stroke)
            ctx.setLineWidth(1)
            ctx.drawPath(using: .fillStroke)
        } else {
            ctx.fillPath()
        }
    }
}
