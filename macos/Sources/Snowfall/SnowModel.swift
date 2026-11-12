import AppKit
import QuartzCore

/// Owns the simulation, the overlay windows and the frame loop.
final class SnowModel {
    let settings = Settings()
    let tracker = WindowTracker()
    let decorations = Decorations()
    lazy var sim = Simulation(settings: settings)

    private(set) var time: CGFloat = 0
    private var overlays: [OverlayWindow] = []
    private var views: [SnowView] = []
    private var timer: Timer?
    private var clickMonitor: Any?
    private var last = CACurrentMediaTime()
    private var lastWindows: CFTimeInterval = 0
    private let startTime = CACurrentMediaTime()

    func start() {
        buildOverlays()
        NotificationCenter.default.addObserver(forName: NSApplication.didChangeScreenParametersNotification,
                                               object: nil, queue: .main) { [weak self] _ in self?.buildOverlays() }

        // Observe clicks elsewhere (never consumed): a click on a pile crumbles it.
        clickMonitor = NSEvent.addGlobalMonitorForEvents(matching: .leftMouseDown) { [weak self] _ in
            guard let self, self.settings.enabled, self.settings.piles else { return }
            self.sim.crumble(at: ScreenArea.toCG(NSEvent.mouseLocation))
        }

        let timer = Timer(timeInterval: 1.0 / 45.0, repeats: true) { [weak self] _ in self?.tick() }
        RunLoop.main.add(timer, forMode: .common) // keep animating while the status menu is open
        self.timer = timer
    }

    private func buildOverlays() {
        overlays.forEach { $0.orderOut(nil) }
        overlays.removeAll()
        views.removeAll()

        let screens = NSScreen.screens
        let areas = screens.map { ScreenArea($0) }
        sim.setScreens(areas)
        for (screen, area) in zip(screens, areas) {
            let view = SnowView(area: area, model: self)
            let window = OverlayWindow(screen: screen, view: view)
            window.orderFrontRegardless()
            overlays.append(window)
            views.append(view)
        }
    }

    private func tick() {
        let now = CACurrentMediaTime()
        let dt = CGFloat(min(max(now - last, 0), 0.05))
        last = now
        time = CGFloat(now - startTime)

        if !settings.enabled {
            return
        }

        if now - lastWindows > 0.1 {
            lastWindows = now
            if settings.piles {
                tracker.refresh(screens: sim.screens)
                sim.syncPiles(tracker.windows)
            }
        }

        sim.step(dt: dt, t: time)
        views.forEach { $0.needsDisplay = true }
    }

    // MARK: - actions used by the menu

    func setEnabled(_ on: Bool) {
        settings.enabled = on
        views.forEach { $0.needsDisplay = true } // clears the overlay when switched off
    }

    func setFalling(_ on: Bool) {
        settings.falling = on
        if on && !settings.enabled { setEnabled(true) }
    }

    func setPiles(_ on: Bool) {
        settings.piles = on
        if !on { sim.clearPiles() }
    }

    func setIntensity(_ level: Intensity) {
        settings.intensity = level
        sim.applyIntensity()
    }
}
