import Foundation

enum Intensity: String, CaseIterable {
    case light, normal, blizzard

    var title: String {
        switch self {
        case .light: return "Лёгкий"
        case .normal: return "Обычный"
        case .blizzard: return "Метель"
        }
    }

    /// Flakes per 1920x1080 points of screen.
    var density: Double {
        switch self {
        case .light: return 70
        case .normal: return 160
        case .blizzard: return 380
        }
    }
}

enum GarlandMode: String, CaseIterable {
    case twinkle, chase, steady

    var title: String {
        switch self {
        case .twinkle: return "Мерцание"
        case .chase: return "Бегущие огни"
        case .steady: return "Ровный свет"
        }
    }
}

/// User settings persisted in UserDefaults.
final class Settings {
    private let defaults = UserDefaults.standard

    init() {
        defaults.register(defaults: [
            "enabled": true, "falling": true, "piles": true, "intensity": Intensity.normal.rawValue,
            "garland": true, "garlandMode": GarlandMode.twinkle.rawValue, "snowman": true, "tree": true,
        ])
    }

    /// Everything on/off ("Зима").
    var enabled: Bool { get { defaults.bool(forKey: "enabled") } set { defaults.set(newValue, forKey: "enabled") } }
    /// Falling snow only; piles and decorations stay.
    var falling: Bool { get { defaults.bool(forKey: "falling") } set { defaults.set(newValue, forKey: "falling") } }
    var piles: Bool { get { defaults.bool(forKey: "piles") } set { defaults.set(newValue, forKey: "piles") } }
    var garland: Bool { get { defaults.bool(forKey: "garland") } set { defaults.set(newValue, forKey: "garland") } }
    var snowman: Bool { get { defaults.bool(forKey: "snowman") } set { defaults.set(newValue, forKey: "snowman") } }
    var tree: Bool { get { defaults.bool(forKey: "tree") } set { defaults.set(newValue, forKey: "tree") } }

    var intensity: Intensity {
        get { Intensity(rawValue: defaults.string(forKey: "intensity") ?? "") ?? .normal }
        set { defaults.set(newValue.rawValue, forKey: "intensity") }
    }

    var garlandMode: GarlandMode {
        get { GarlandMode(rawValue: defaults.string(forKey: "garlandMode") ?? "") ?? .twinkle }
        set { defaults.set(newValue.rawValue, forKey: "garlandMode") }
    }
}
