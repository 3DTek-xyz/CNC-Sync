import Foundation
import ServiceManagement

private var lastErrorMessage = ""

private func statusCode(_ status: SMAppService.Status) -> Int32 {
    switch status {
    case .notRegistered, .notFound:
        return 0
    case .enabled:
        return 1
    case .requiresApproval:
        return 2
    @unknown default:
        return -1
    }
}

private func setLastError(_ error: Error) -> Int32 {
    lastErrorMessage = String(describing: error)
    return -1
}

@_cdecl("cnc_sync_login_item_status")
public func cnc_sync_login_item_status() -> Int32 {
    guard #available(macOS 13.0, *) else {
        lastErrorMessage = "SMAppService requires macOS 13 or later."
        return -2
    }

    return statusCode(SMAppService.mainApp.status)
}

@_cdecl("cnc_sync_login_item_enable")
public func cnc_sync_login_item_enable() -> Int32 {
    guard #available(macOS 13.0, *) else {
        lastErrorMessage = "SMAppService requires macOS 13 or later."
        return -2
    }

    do {
        try SMAppService.mainApp.register()
        return statusCode(SMAppService.mainApp.status)
    } catch {
        return setLastError(error)
    }
}

@_cdecl("cnc_sync_login_item_disable")
public func cnc_sync_login_item_disable() -> Int32 {
    guard #available(macOS 13.0, *) else {
        lastErrorMessage = "SMAppService requires macOS 13 or later."
        return -2
    }

    do {
        try SMAppService.mainApp.unregister()
        return statusCode(SMAppService.mainApp.status)
    } catch {
        return setLastError(error)
    }
}

@_cdecl("cnc_sync_login_item_open_settings")
public func cnc_sync_login_item_open_settings() -> Int32 {
    guard #available(macOS 13.0, *) else {
        lastErrorMessage = "SMAppService requires macOS 13 or later."
        return -2
    }

    SMAppService.openSystemSettingsLoginItems()
    return 0
}

@_cdecl("cnc_sync_login_item_copy_last_error")
public func cnc_sync_login_item_copy_last_error() -> UnsafeMutablePointer<CChar>? {
    strdup(lastErrorMessage)
}

@_cdecl("cnc_sync_login_item_free_string")
public func cnc_sync_login_item_free_string(_ pointer: UnsafeMutablePointer<CChar>?) {
    free(pointer)
}
