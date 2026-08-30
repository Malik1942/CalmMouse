import Foundation

/// Looks up `key` in the app bundle's Localizable.strings, formatting any arguments in.
///
/// SwiftUI string literals localize themselves (they become `LocalizedStringKey`s resolved
/// against the main bundle), so this is only for the strings SwiftUI can't reach: AppKit
/// titles and tooltips, `String`-typed view parameters, and anything built with
/// interpolation. Keys are the English text, so a missing table entry — or running the bare
/// binary outside the .app bundle, where no .lproj exists — falls back to English.
func L(_ key: String, _ args: CVarArg...) -> String {
    let localized = NSLocalizedString(key, comment: "")
    return args.isEmpty ? localized : String(format: localized, arguments: args)
}
