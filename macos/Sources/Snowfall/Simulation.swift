import CoreGraphics
import Foundation

@inline(__always) private func rnd() -> CGFloat { CGFloat.random(in: 0..<1) }

struct Flake {
    var x: CGFloat, y: CGFloat, prevY: CGFloat, r: CGFloat
    var vx: CGFloat, vy: CGFloat, phase: CGFloat, freq: CGFloat, sway: CGFloat, depth: CGFloat
    var alpha: CGFloat
    var screen: Int
    /// Snow knocked off a pile: falls once, never respawns.
    var transient = false
    var dead = false
}

final class Pile {
    let id: Int
    var rect: CGRect
    var h: [CGFloat]
    var exposed: [(CGFloat, CGFloat)] = []
    var seen = false

    init(id: Int, rect: CGRect) {
        self.id = id
        self.rect = rect
        h = [CGFloat](repeating: 0, count: max(0, Int(rect.width)))
    }
}

/// Snow particles and window-edge snow piles, in CG space points (y down).
final class Simulation {
    private let maxTransient = 1500
    private let settings: Settings
    private var wind: CGFloat = 0

    var screens: [ScreenArea] = []
    var flakes: [Flake] = []
    var piles: [Int: Pile] = [:]

    init(settings: Settings) { self.settings = settings }

    func setScreens(_ areas: [ScreenArea]) {
        screens = areas
        flakes.removeAll()
        applyIntensity(initialFill: true)
    }

    func applyIntensity(initialFill: Bool = false) {
        for i in screens.indices {
            let f = screens[i].frame
            screens[i].target = Int(settings.intensity.density * Double(f.width * f.height) / (1920.0 * 1080.0))
            if initialFill && settings.falling {
                for _ in 0..<screens[i].target { flakes.append(spawn(i, anywhere: true)) }
            }
        }
    }

    func step(dt: CGFloat, t: CGFloat) {
        let windBase: CGFloat = settings.intensity == .blizzard ? 60 : 18
        wind = windBase * (0.6 * sin(t * 0.07) + 0.4 * sin(t * 0.23 + 1.3))

        var counts = [Int](repeating: 0, count: screens.count)
        for f in flakes where !f.transient && !f.dead { counts[f.screen] += 1 }
        // At most two new flakes per screen per step, so resuming snowfall ramps up instead of dropping a wall.
        if settings.falling {
            for i in screens.indices {
                var k = counts[i], added = 0
                while k < screens[i].target && added < 2 {
                    flakes.append(spawn(i, anywhere: false))
                    k += 1
                    added += 1
                }
            }
        }

        let pilesOn = settings.piles
        for i in flakes.indices {
            var f = flakes[i]
            if f.dead { continue }
            let b = screens[f.screen].frame

            // Snowfall switched off: flakes in the air fade out within about a second.
            if !settings.falling && !f.transient {
                f.alpha -= 0.9 * dt
                if f.alpha < 0.27 { f.dead = true; flakes[i] = f; continue }
            }

            f.prevY = f.y
            if f.transient {
                // Clumps knocked off a pile: gravity plus a decaying sideways kick.
                f.vy = min(f.vy + 380 * dt, 420)
                f.x += f.vx * dt
                f.vx *= max(0, 1 - 2.5 * dt)
            }
            f.y += f.vy * dt
            f.x += (wind * (0.35 + 0.65 * f.depth) + sin(t * f.freq + f.phase) * f.sway) * dt

            // Knocked-off clumps fall through: otherwise they'd land straight back on the pile they came from.
            if pilesOn && !f.transient && f.depth > 0.35 && tryLand(f) {
                recycle(&f)
            } else if f.y - f.r > b.maxY {
                recycle(&f)
            } else if !f.transient {
                if f.x < b.minX - 4 { f.x += b.width + 8 } else if f.x > b.maxX + 4 { f.x -= b.width + 8 }
            }
            flakes[i] = f
        }

        // Drop dead flakes and the surplus left after lowering the intensity.
        var seen = [Int](repeating: 0, count: screens.count)
        let falling = settings.falling
        let targets = screens.map { $0.target }
        flakes.removeAll { f in
            if f.dead { return true }
            if f.transient || !falling { return false }
            seen[f.screen] += 1
            return seen[f.screen] > targets[f.screen]
        }
    }

    private func recycle(_ f: inout Flake) {
        if f.transient || !settings.falling { f.dead = true; return }
        let fresh = spawn(f.screen, anywhere: false)
        f.x = fresh.x
        f.y = fresh.y
        f.prevY = fresh.y
    }

    private func tryLand(_ f: Flake) -> Bool {
        for p in piles.values {
            let left = p.rect.minX
            if f.x < left || f.x >= left + CGFloat(p.h.count) || p.exposed.isEmpty { continue }
            let col = Int(f.x - left)
            let surface = p.rect.minY - p.h[col]
            if !(f.prevY < surface && f.y >= surface) { continue }
            guard p.exposed.contains(where: { f.x >= $0.0 && f.x < $0.1 }) else { continue }
            if rnd() > 0.9 { return false } // some flakes fall past in front
            return deposit(p, col: col, radius: f.r)
        }
        return false
    }

    private func deposit(_ p: Pile, col: Int, radius: CGFloat) -> Bool {
        let len = p.h.count
        let maxH: CGFloat = 13
        let amount = radius * 0.75
        let sigma = max(1.5, radius * 1.3)
        let w = Int(sigma * 2.5)
        var any = false
        for k in -w...w {
            let i = col + k
            if i < 0 || i >= len { continue }
            // Taper towards the window corners so the pile rounds off instead of ending in a wall.
            let edge = CGFloat(min(i, len - 1 - i))
            let cap = min(maxH, 1.5 + edge * 0.45)
            if p.h[i] >= cap { continue }
            p.h[i] = min(cap, p.h[i] + amount * exp(-CGFloat(k * k) / (2 * sigma * sigma)))
            any = true
        }
        return any
    }

    /// Match piles to the current window list: follow, shake off moved windows, drop closed ones.
    func syncPiles(_ windows: [TrackedWindow]) {
        for p in piles.values { p.seen = false }

        for w in windows {
            if let p = piles[w.id] {
                let o = p.rect
                if Int(o.minX) != Int(w.rect.minX) || Int(o.minY) != Int(w.rect.minY) || Int(o.width) != Int(w.rect.width) {
                    shakeOff(p)
                    p.h = [CGFloat](repeating: 0, count: max(0, Int(w.rect.width)))
                }
                p.rect = w.rect
                p.exposed = w.exposed
                p.seen = true
                relax(p)
            } else if !w.exposed.isEmpty {
                let p = Pile(id: w.id, rect: w.rect)
                p.exposed = w.exposed
                p.seen = true
                piles[w.id] = p
            }
        }

        for (id, p) in piles where !p.seen {
            shakeOff(p)
            piles.removeValue(forKey: id)
        }
    }

    func shakeOffAll() {
        for p in piles.values { shakeOff(p) }
    }

    func clearPiles() { piles.removeAll() }

    private func shakeOff(_ p: Pile) { crumble(p, from: 0, to: p.h.count, burst: 25) }

    /// If the point hits a visible pile, knock the snow around it off with a little burst.
    @discardableResult
    func crumble(at pt: CGPoint) -> Bool {
        for p in piles.values {
            let left = p.rect.minX
            if pt.x < left || pt.x >= left + CGFloat(p.h.count) { continue }
            guard p.exposed.contains(where: { pt.x >= $0.0 && pt.x < $0.1 }) else { continue }
            let col = Int(pt.x - left)
            var h: CGFloat = 0
            for i in max(0, col - 6)...min(p.h.count - 1, col + 6) { h = max(h, p.h[i]) }
            if h < 1.5 { continue }
            if pt.y < p.rect.minY - h - 5 || pt.y > p.rect.minY + 3 { continue }
            crumble(p, from: col - 45, to: col + 45, burst: 140)
            return true
        }
        return false
    }

    /// Turn the pile between columns [from, to) into falling clumps and clear it.
    private func crumble(_ p: Pile, from: Int, to: Int, burst: CGFloat) {
        let a = max(0, from), b = min(p.h.count, to)
        if b <= a { return }
        let screen = screenIndex(of: CGPoint(x: p.rect.minX + CGFloat(a + b) / 2, y: p.rect.minY))
        var transient = flakes.filter { $0.transient }.count
        let step = max(2, (b - a) / 160)
        let mid = CGFloat(a + b) / 2
        var i = a
        while i < b && transient < maxTransient {
            let h = p.h[i]
            if h >= 0.8 {
                let n = 1 + Int(h / 4)
                let dir: CGFloat = CGFloat(i) < mid ? -1 : 1
                for _ in 0..<n {
                    flakes.append(Flake(
                        x: p.rect.minX + CGFloat(i) + rnd() * CGFloat(step),
                        y: p.rect.minY - rnd() * h,
                        prevY: p.rect.minY,
                        r: 0.9 + 1.8 * rnd(),
                        vx: (dir * 0.6 + (rnd() - 0.5)) * burst,
                        vy: -burst * 0.6 * rnd() + 20,
                        phase: rnd() * 2 * .pi, freq: 0.8 + rnd(), sway: 2 + 6 * rnd(),
                        depth: 0.6 + 0.4 * rnd(),
                        alpha: 0.8 + 0.2 * rnd(),
                        screen: screen, transient: true))
                    transient += 1
                }
            }
            i += step
        }
        for k in a..<b { p.h[k] = 0 }
        relax(p)
    }

    /// Angle of repose: let steep steps slide down to the neighbours.
    private func relax(_ p: Pile) {
        let limit: CGFloat = 1.1
        guard p.h.count > 1 else { return }
        for _ in 0..<2 {
            for i in 0..<(p.h.count - 1) {
                let d = p.h[i] - p.h[i + 1]
                if abs(d) <= limit { continue }
                let move = (d - (d > 0 ? limit : -limit)) * 0.25
                p.h[i] -= move
                p.h[i + 1] += move
            }
        }
    }

    private func screenIndex(of pt: CGPoint) -> Int {
        if let i = screens.firstIndex(where: { $0.frame.contains(pt) }) { return i }
        var best = 0
        var bestD = CGFloat.greatestFiniteMagnitude
        for (i, s) in screens.enumerated() {
            let dx = max(0, max(s.frame.minX - pt.x, pt.x - s.frame.maxX))
            let dy = max(0, max(s.frame.minY - pt.y, pt.y - s.frame.maxY))
            if dx * dx + dy * dy < bestD { bestD = dx * dx + dy * dy; best = i }
        }
        return best
    }

    private func spawn(_ screen: Int, anywhere: Bool) -> Flake {
        let b = screens[screen].frame
        // Depth 0 = far (small, slow, faint), 1 = near (big, fast, bright).
        let d = pow(rnd(), 1.6)
        let r = 0.7 + 2.6 * d
        let y = anywhere ? b.minY + rnd() * b.height : b.minY - r - rnd() * 40
        return Flake(
            x: b.minX + rnd() * b.width, y: y, prevY: y, r: r,
            vx: 0, vy: 22 + 48 * d + 8 * rnd(),
            phase: rnd() * 2 * .pi, freq: 0.4 + 1.2 * rnd(), sway: 6 + 22 * rnd(),
            depth: d, alpha: (110 + 145 * d) / 255, screen: screen)
    }
}
