import AppKit
import ServiceManagement

/// Menu bar item and its menu. The app has no Dock icon (LSUIElement).
final class AppDelegate: NSObject, NSApplicationDelegate, NSMenuDelegate {
    private let model = SnowModel()
    private var statusItem: NSStatusItem!

    func applicationDidFinishLaunching(_ notification: Notification) {
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        if let button = statusItem.button {
            button.image = NSImage(systemSymbolName: "snowflake", accessibilityDescription: "Snowfall")
            button.image?.isTemplate = true
        }
        let menu = NSMenu()
        menu.delegate = self
        statusItem.menu = menu

        model.start()
    }

    // Rebuilt each time it opens so checkmarks always match the settings.
    func menuNeedsUpdate(_ menu: NSMenu) {
        menu.removeAllItems()
        let s = model.settings

        menu.addItem(item("Зима (снег, сугробы, украшения)", #selector(toggleEnabled), on: s.enabled))
        menu.addItem(item("Снегопад", #selector(toggleFalling), on: s.falling))

        let intensity = NSMenu()
        for level in Intensity.allCases {
            let it = item(level.title, #selector(chooseIntensity(_:)), on: s.intensity == level)
            it.representedObject = level.rawValue
            intensity.addItem(it)
        }
        menu.addItem(submenu("Сила снега", intensity))

        menu.addItem(item("Сугробы на окнах", #selector(togglePiles), on: s.piles))
        menu.addItem(item("Стряхнуть сугробы", #selector(shake)))

        let decor = NSMenu()
        decor.addItem(item("Гирлянда под строкой меню", #selector(toggleGarland), on: s.garland))
        let modes = NSMenu()
        for mode in GarlandMode.allCases {
            let it = item(mode.title, #selector(chooseMode(_:)), on: s.garlandMode == mode)
            it.representedObject = mode.rawValue
            modes.addItem(it)
        }
        decor.addItem(submenu("Режим гирлянды", modes))
        decor.addItem(.separator())
        decor.addItem(item("Снеговик", #selector(toggleSnowman), on: s.snowman))
        decor.addItem(item("Ёлка", #selector(toggleTree), on: s.tree))
        menu.addItem(submenu("Украшения", decor))

        menu.addItem(.separator())
        menu.addItem(item("Открывать при входе в систему", #selector(toggleLogin),
                          on: SMAppService.mainApp.status == .enabled))
        menu.addItem(.separator())
        menu.addItem(NSMenuItem(title: "Выход", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q"))
    }

    private func item(_ title: String, _ action: Selector, on: Bool? = nil) -> NSMenuItem {
        let it = NSMenuItem(title: title, action: action, keyEquivalent: "")
        it.target = self
        if let on { it.state = on ? .on : .off }
        return it
    }

    private func submenu(_ title: String, _ menu: NSMenu) -> NSMenuItem {
        let it = NSMenuItem(title: title, action: nil, keyEquivalent: "")
        it.submenu = menu
        return it
    }

    @objc private func toggleEnabled() { model.setEnabled(!model.settings.enabled) }
    @objc private func toggleFalling() { model.setFalling(!model.settings.falling) }
    @objc private func togglePiles() { model.setPiles(!model.settings.piles) }
    @objc private func shake() { model.sim.shakeOffAll() }
    @objc private func toggleGarland() { model.settings.garland.toggle() }
    @objc private func toggleSnowman() { model.settings.snowman.toggle() }
    @objc private func toggleTree() { model.settings.tree.toggle() }

    @objc private func chooseIntensity(_ sender: NSMenuItem) {
        if let raw = sender.representedObject as? String, let level = Intensity(rawValue: raw) { model.setIntensity(level) }
    }

    @objc private func chooseMode(_ sender: NSMenuItem) {
        if let raw = sender.representedObject as? String, let mode = GarlandMode(rawValue: raw) { model.settings.garlandMode = mode }
    }

    @objc private func toggleLogin() {
        do {
            if SMAppService.mainApp.status == .enabled {
                try SMAppService.mainApp.unregister()
            } else {
                try SMAppService.mainApp.register()
            }
        } catch {
            let alert = NSAlert()
            alert.messageText = "Не удалось изменить автозапуск"
            alert.informativeText = error.localizedDescription
            alert.runModal()
        }
    }
}
