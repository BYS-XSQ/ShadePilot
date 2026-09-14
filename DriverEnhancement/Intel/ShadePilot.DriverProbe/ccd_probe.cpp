#include <windows.h>
#include <wingdi.h>
#include <fstream>
#include <iomanip>
#include <iostream>
#include <cstdlib>
#include <chrono>
#include <thread>
#include <cstring>
#include <sstream>
#include <string>
#include <vector>

namespace {
constexpr UINT32 kDefaultQueryFlags = QDC_ONLY_ACTIVE_PATHS | QDC_VIRTUAL_MODE_AWARE | QDC_VIRTUAL_REFRESH_RATE_AWARE;
UINT32 QueryFlags() {
    const char* raw = std::getenv("SP_CCD_QUERY_FLAGS");
    if (!raw || !*raw) return kDefaultQueryFlags;
    char* end = nullptr; const unsigned long parsed = std::strtoul(raw, &end, 0);
    return (end && *end == '\0') ? static_cast<UINT32>(parsed) : kDefaultQueryFlags;
}
struct CcdSnapshot {
    UINT32 pathCount{};
    UINT32 modeCount{};
    DISPLAYCONFIG_TOPOLOGY_ID topology{};
    std::vector<DISPLAYCONFIG_PATH_INFO> paths;
    std::vector<DISPLAYCONFIG_MODE_INFO> modes;
};
UINT32 SourceIndex(const DISPLAYCONFIG_PATH_INFO& p);
UINT32 TargetIndex(const DISPLAYCONFIG_PATH_INFO& p);
UINT32 DesktopIndex(const DISPLAYCONFIG_PATH_INFO& p);
std::string Hex(DWORD v) { std::ostringstream o; o << "0x" << std::uppercase << std::hex << v; return o.str(); }
std::string Luid(const LUID& l) { std::ostringstream o; o << std::uppercase << std::hex << l.HighPart << ":" << l.LowPart; return o.str(); }
const char* InfoType(DISPLAYCONFIG_MODE_INFO_TYPE t) {
    switch (t) { case DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE: return "SOURCE"; case DISPLAYCONFIG_MODE_INFO_TYPE_TARGET: return "TARGET"; case DISPLAYCONFIG_MODE_INFO_TYPE_DESKTOP_IMAGE: return "DESKTOP_IMAGE"; default: return "OTHER"; }
}
const char* OutputTech(DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY t) {
    switch (t) { case DISPLAYCONFIG_OUTPUT_TECHNOLOGY_HDMI: return "HDMI"; case DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EXTERNAL: return "DisplayPort"; case DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EMBEDDED: return "eDP"; case DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL: return "Internal"; default: return "Other"; }
}
bool Query(CcdSnapshot& s, LONG& error) {
    const UINT32 flags = QueryFlags();
    error = GetDisplayConfigBufferSizes(flags, &s.pathCount, &s.modeCount);
    if (error != ERROR_SUCCESS) { std::cerr << "GetDisplayConfigBufferSizes flags=" << Hex(flags) << " failed: " << Hex(error) << "\n"; return false; }
    std::cerr << "GetDisplayConfigBufferSizes flags=" << Hex(flags) << " paths=" << s.pathCount << " modes=" << s.modeCount << "\n";
    s.paths.resize(s.pathCount); s.modes.resize(s.modeCount);
    // pCurrentTopologyId is optional for active/all path queries; omit it to avoid
    // ERROR_INVALID_PARAMETER on drivers that do not expose a topology id here.
    error = QueryDisplayConfig(flags, &s.pathCount, s.paths.data(), &s.modeCount, s.modes.data(), nullptr);
    if (error != ERROR_SUCCESS) { std::cerr << "QueryDisplayConfig flags=" << Hex(flags) << " failed: " << Hex(error) << "\n"; return false; }
    s.paths.resize(s.pathCount); s.modes.resize(s.modeCount); return true;
}
bool SaveBinary(const std::string& jsonPath, const CcdSnapshot& s) {
    std::ofstream f(jsonPath + ".ccd.bin", std::ios::binary | std::ios::trunc); if (!f) return false;
    f.write(reinterpret_cast<const char*>(&s.pathCount), sizeof(s.pathCount));
    f.write(reinterpret_cast<const char*>(&s.modeCount), sizeof(s.modeCount));
    f.write(reinterpret_cast<const char*>(&s.topology), sizeof(s.topology));
    f.write(reinterpret_cast<const char*>(s.paths.data()), sizeof(DISPLAYCONFIG_PATH_INFO) * s.paths.size());
    f.write(reinterpret_cast<const char*>(s.modes.data()), sizeof(DISPLAYCONFIG_MODE_INFO) * s.modes.size());
    return static_cast<bool>(f);
}
bool LoadBinary(const std::string& jsonPath, CcdSnapshot& s) {
    std::ifstream f(jsonPath + ".ccd.bin", std::ios::binary); if (!f) return false;
    f.read(reinterpret_cast<char*>(&s.pathCount), sizeof(s.pathCount));
    f.read(reinterpret_cast<char*>(&s.modeCount), sizeof(s.modeCount));
    f.read(reinterpret_cast<char*>(&s.topology), sizeof(s.topology));
    if (!f || s.pathCount > 64 || s.modeCount > 256) return false;
    s.paths.resize(s.pathCount); s.modes.resize(s.modeCount);
    f.read(reinterpret_cast<char*>(s.paths.data()), sizeof(DISPLAYCONFIG_PATH_INFO) * s.paths.size());
    f.read(reinterpret_cast<char*>(s.modes.data()), sizeof(DISPLAYCONFIG_MODE_INFO) * s.modes.size());
    return static_cast<bool>(f);
}
UINT32 SourceIndex(const DISPLAYCONFIG_PATH_INFO& p) { return (p.flags & DISPLAYCONFIG_PATH_SUPPORT_VIRTUAL_MODE) ? p.sourceInfo.sourceModeInfoIdx : p.sourceInfo.modeInfoIdx; }
UINT32 TargetIndex(const DISPLAYCONFIG_PATH_INFO& p) { return (p.flags & DISPLAYCONFIG_PATH_SUPPORT_VIRTUAL_MODE) ? p.targetInfo.targetModeInfoIdx : p.targetInfo.modeInfoIdx; }
UINT32 DesktopIndex(const DISPLAYCONFIG_PATH_INFO& p) { return (p.flags & DISPLAYCONFIG_PATH_SUPPORT_VIRTUAL_MODE) ? p.targetInfo.desktopModeInfoIdx : DISPLAYCONFIG_PATH_DESKTOP_IMAGE_IDX_INVALID; }
LONG ValidateRoundTrip(const CcdSnapshot& original) {
    CcdSnapshot copy = original;
    return SetDisplayConfig(copy.pathCount, copy.paths.data(), copy.modeCount, copy.modes.data(),
        SDC_VALIDATE | SDC_USE_SUPPLIED_DISPLAY_CONFIG | SDC_VIRTUAL_MODE_AWARE | SDC_VIRTUAL_REFRESH_RATE_AWARE);
}
LONG ApplyConfig(const CcdSnapshot& config, bool noOptimization) {
    CcdSnapshot copy = config;
    UINT32 flags = SDC_APPLY | SDC_USE_SUPPLIED_DISPLAY_CONFIG | SDC_VIRTUAL_MODE_AWARE | SDC_VIRTUAL_REFRESH_RATE_AWARE;
    if (noOptimization) flags |= SDC_NO_OPTIMIZATION;
    return SetDisplayConfig(copy.pathCount, copy.paths.data(), copy.modeCount, copy.modes.data(), flags);
}
LONG RestoreConfig(const CcdSnapshot& baseline) { return ApplyConfig(baseline, false); }
bool StartRestoreWatchdog(const std::string& baselinePath, unsigned seconds) {
    char module[MAX_PATH]{}; const DWORD n = GetModuleFileNameA(nullptr, module, MAX_PATH); if (!n || n >= MAX_PATH) return false;
    std::string command = "\"" + std::string(module, n) + "\" restore-after \"" + baselinePath + "\" " + std::to_string(seconds);
    std::vector<char> mutableCommand(command.begin(), command.end()); mutableCommand.push_back('\0');
    STARTUPINFOA si{}; si.cb = sizeof(si); PROCESS_INFORMATION pi{};
    const BOOL ok = CreateProcessA(nullptr, mutableCommand.data(), nullptr, nullptr, FALSE, CREATE_NO_WINDOW, nullptr, nullptr, &si, &pi);
    if (ok) { CloseHandle(pi.hThread); CloseHandle(pi.hProcess); }
    return ok == TRUE;
}
bool IsTarget720p(const CcdSnapshot& s, int pathIndex) {
    if (pathIndex < 0 || static_cast<UINT32>(pathIndex) >= s.pathCount) return false;
    const auto& p = s.paths[static_cast<size_t>(pathIndex)]; const UINT32 si = SourceIndex(p), di = DesktopIndex(p), ti = TargetIndex(p);
    if (si >= s.modeCount || ti >= s.modeCount || s.modes[si].infoType != DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE || s.modes[ti].infoType != DISPLAYCONFIG_MODE_INFO_TYPE_TARGET) return false;
    if (s.modes[si].sourceMode.width != 1280 || s.modes[si].sourceMode.height != 720) return false;
    if (s.modes[ti].targetMode.targetVideoSignalInfo.activeSize.cx != 1920 || s.modes[ti].targetMode.targetVideoSignalInfo.activeSize.cy != 1080) return false;
    if ((p.flags & DISPLAYCONFIG_PATH_SUPPORT_VIRTUAL_MODE) && (di >= s.modeCount || s.modes[di].infoType != DISPLAYCONFIG_MODE_INFO_TYPE_DESKTOP_IMAGE || s.modes[di].desktopImageInfo.PathSourceSize.x != 1280 || s.modes[di].desktopImageInfo.PathSourceSize.y != 720)) return false;
    return p.targetInfo.scaling == DISPLAYCONFIG_SCALING_STRETCHED;
}
bool SameConfig(const CcdSnapshot& a, const CcdSnapshot& b) {
    if (a.pathCount != b.pathCount || a.modeCount != b.modeCount) return false;
    return std::memcmp(a.paths.data(), b.paths.data(), sizeof(DISPLAYCONFIG_PATH_INFO) * a.pathCount) == 0 &&
           std::memcmp(a.modes.data(), b.modes.data(), sizeof(DISPLAYCONFIG_MODE_INFO) * a.modeCount) == 0;
}
bool Build720pCandidate(const CcdSnapshot& base, CcdSnapshot& candidate, int& candidatePath, std::string& reason) {
    candidate = base; candidatePath = -1;
    for (UINT32 i = 0; i < candidate.pathCount; ++i) if ((candidate.paths[i].flags & DISPLAYCONFIG_PATH_ACTIVE) && candidate.paths[i].targetInfo.outputTechnology == DISPLAYCONFIG_OUTPUT_TECHNOLOGY_HDMI) { candidatePath = static_cast<int>(i); break; }
    if (candidatePath < 0) { reason = "No active HDMI path found"; return false; }
    auto& p = candidate.paths[static_cast<size_t>(candidatePath)]; const UINT32 si = SourceIndex(p), ti = TargetIndex(p), di = DesktopIndex(p);
    if (si >= candidate.modeCount || ti >= candidate.modeCount || candidate.modes[si].infoType != DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE || candidate.modes[ti].infoType != DISPLAYCONFIG_MODE_INFO_TYPE_TARGET) { reason = "Source/target mode indexes invalid"; return false; }
    if ((p.flags & DISPLAYCONFIG_PATH_SUPPORT_VIRTUAL_MODE) && (di >= candidate.modeCount || candidate.modes[di].infoType != DISPLAYCONFIG_MODE_INFO_TYPE_DESKTOP_IMAGE)) { reason = "Virtual desktop mode index invalid"; return false; }
    candidate.modes[si].sourceMode.width = 1280; candidate.modes[si].sourceMode.height = 720;
    if (p.flags & DISPLAYCONFIG_PATH_SUPPORT_VIRTUAL_MODE) {
        auto& desktop = candidate.modes[di].desktopImageInfo;
        desktop.PathSourceSize.x = 1280; desktop.PathSourceSize.y = 720;
        desktop.DesktopImageRegion.right = desktop.DesktopImageRegion.left + 1280;
        desktop.DesktopImageRegion.bottom = desktop.DesktopImageRegion.top + 720;
        desktop.DesktopImageClip.right = desktop.DesktopImageClip.left + 1280;
        desktop.DesktopImageClip.bottom = desktop.DesktopImageClip.top + 720;
    }
    p.targetInfo.scaling = DISPLAYCONFIG_SCALING_STRETCHED;
    return true;
}
void PrintSnapshot(const CcdSnapshot& s, const char* title) {
    std::cout << "=== " << title << " ===\n";
    std::cout << "PathCount=" << s.pathCount << " ModeCount=" << s.modeCount << " Topology=" << s.topology << "\n";
    for (UINT32 i = 0; i < s.pathCount; ++i) {
        const auto& p = s.paths[i]; const UINT32 si = SourceIndex(p); const UINT32 ti = TargetIndex(p);
        std::cout << "Path[" << i << "] flags=" << Hex(p.flags) << " active=" << ((p.flags & DISPLAYCONFIG_PATH_ACTIVE) ? "true" : "false")
                  << " virtualAware=" << ((p.flags & DISPLAYCONFIG_PATH_SUPPORT_VIRTUAL_MODE) ? "true" : "false")
                  << " source(adapter=" << Luid(p.sourceInfo.adapterId) << ",id=" << p.sourceInfo.id << ",mode=" << si << ",raw=" << p.sourceInfo.modeInfoIdx << ",clone=" << p.sourceInfo.cloneGroupId << ")"
                  << " target(adapter=" << Luid(p.targetInfo.adapterId) << ",id=" << p.targetInfo.id << ",mode=" << ti << ",raw=" << p.targetInfo.modeInfoIdx << ",desktop=" << DesktopIndex(p) << ")"
                  << " output=" << OutputTech(p.targetInfo.outputTechnology)
                  << " scaling=" << p.targetInfo.scaling
                  << " refresh=" << p.targetInfo.refreshRate.Numerator << "/" << p.targetInfo.refreshRate.Denominator << "\n";
    }
    for (UINT32 i = 0; i < s.modeCount; ++i) {
        const auto& m = s.modes[i]; std::cout << "Mode[" << i << "] type=" << InfoType(m.infoType) << " id=" << m.id << " adapter=" << Luid(m.adapterId);
        if (m.infoType == DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE) std::cout << " size=" << m.sourceMode.width << "x" << m.sourceMode.height << " pixelFormat=" << m.sourceMode.pixelFormat << " pos=" << m.sourceMode.position.x << "," << m.sourceMode.position.y;
        else if (m.infoType == DISPLAYCONFIG_MODE_INFO_TYPE_TARGET) std::cout << " active=" << m.targetMode.targetVideoSignalInfo.activeSize.cx << "x" << m.targetMode.targetVideoSignalInfo.activeSize.cy << " total=" << m.targetMode.targetVideoSignalInfo.totalSize.cx << "x" << m.targetMode.targetVideoSignalInfo.totalSize.cy << " pixelRate=" << m.targetMode.targetVideoSignalInfo.pixelRate;
        else if (m.infoType == DISPLAYCONFIG_MODE_INFO_TYPE_DESKTOP_IMAGE) std::cout << " pathSourceSize=" << m.desktopImageInfo.PathSourceSize.x << "x" << m.desktopImageInfo.PathSourceSize.y
            << " region=" << m.desktopImageInfo.DesktopImageRegion.left << "," << m.desktopImageInfo.DesktopImageRegion.top << "," << m.desktopImageInfo.DesktopImageRegion.right << "," << m.desktopImageInfo.DesktopImageRegion.bottom
            << " clip=" << m.desktopImageInfo.DesktopImageClip.left << "," << m.desktopImageInfo.DesktopImageClip.top << "," << m.desktopImageInfo.DesktopImageClip.right << "," << m.desktopImageInfo.DesktopImageClip.bottom;
        std::cout << "\n";
    }
}
bool WriteJson(const std::string& path, const CcdSnapshot& s, const char* stage, LONG validationResult = ERROR_SUCCESS, LONG gate0Result = -1) {
    std::ofstream f(path, std::ios::trunc); if (!f) return false;
    f << "{\n  \"stage\": \"" << stage << "\",\n  \"pathCount\": " << s.pathCount << ",\n  \"modeCount\": " << s.modeCount << ",\n  \"topology\": " << s.topology << ",\n  \"gate0Result\": " << gate0Result << ",\n  \"validationResult\": " << validationResult << ",\n  \"paths\": [\n";
    for (UINT32 i = 0; i < s.pathCount; ++i) {
        if (i) f << ",\n"; const auto& p = s.paths[i];
        f << "    {\"index\":" << i << ",\"flags\":" << p.flags << ",\"active\":" << ((p.flags & DISPLAYCONFIG_PATH_ACTIVE) ? "true" : "false")
          << ",\"virtualAware\":" << ((p.flags & DISPLAYCONFIG_PATH_SUPPORT_VIRTUAL_MODE) ? "true" : "false")
          << ",\"sourceModeInfoIdx\":" << SourceIndex(p) << ",\"targetModeInfoIdx\":" << TargetIndex(p) << ",\"desktopModeInfoIdx\":" << DesktopIndex(p)
          << ",\"sourceId\":" << p.sourceInfo.id << ",\"targetId\":" << p.targetInfo.id
          << ",\"outputTechnology\":" << p.targetInfo.outputTechnology << ",\"windowsScaling\":" << p.targetInfo.scaling
          << ",\"refreshNumerator\":" << p.targetInfo.refreshRate.Numerator << ",\"refreshDenominator\":" << p.targetInfo.refreshRate.Denominator << "}";
    }
    f << "\n  ],\n  \"modes\": [\n";
    for (UINT32 i = 0; i < s.modeCount; ++i) {
        if (i) f << ",\n"; const auto& m = s.modes[i];
        f << "    {\"index\":" << i << ",\"infoType\":\"" << InfoType(m.infoType) << "\",\"id\":" << m.id << ",\"adapterHigh\":" << m.adapterId.HighPart << ",\"adapterLow\":" << m.adapterId.LowPart;
        if (m.infoType == DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE) f << ",\"width\":" << m.sourceMode.width << ",\"height\":" << m.sourceMode.height << ",\"pixelFormat\":" << m.sourceMode.pixelFormat << ",\"positionX\":" << m.sourceMode.position.x << ",\"positionY\":" << m.sourceMode.position.y;
        if (m.infoType == DISPLAYCONFIG_MODE_INFO_TYPE_TARGET) f << ",\"activeWidth\":" << m.targetMode.targetVideoSignalInfo.activeSize.cx << ",\"activeHeight\":" << m.targetMode.targetVideoSignalInfo.activeSize.cy << ",\"totalWidth\":" << m.targetMode.targetVideoSignalInfo.totalSize.cx << ",\"totalHeight\":" << m.targetMode.targetVideoSignalInfo.totalSize.cy << ",\"pixelRate\":" << m.targetMode.targetVideoSignalInfo.pixelRate;
        if (m.infoType == DISPLAYCONFIG_MODE_INFO_TYPE_DESKTOP_IMAGE) f << ",\"pathSourceWidth\":" << m.desktopImageInfo.PathSourceSize.x << ",\"pathSourceHeight\":" << m.desktopImageInfo.PathSourceSize.y << ",\"regionLeft\":" << m.desktopImageInfo.DesktopImageRegion.left << ",\"regionTop\":" << m.desktopImageInfo.DesktopImageRegion.top << ",\"regionRight\":" << m.desktopImageInfo.DesktopImageRegion.right << ",\"regionBottom\":" << m.desktopImageInfo.DesktopImageRegion.bottom << ",\"clipLeft\":" << m.desktopImageInfo.DesktopImageClip.left << ",\"clipTop\":" << m.desktopImageInfo.DesktopImageClip.top << ",\"clipRight\":" << m.desktopImageInfo.DesktopImageClip.right << ",\"clipBottom\":" << m.desktopImageInfo.DesktopImageClip.bottom;
        f << "}";
    }
    f << "\n  ]\n}\n"; return true;
}
int Usage() { std::cerr << "Usage:\n  ShadePilot.CcdProbe.exe probe <baseline.json>\n  ShadePilot.CcdProbe.exe validate-720p <validation.json>\n  ShadePilot.CcdProbe.exe apply-720p <baseline.json>\n  ShadePilot.CcdProbe.exe restore <baseline.json>\n  ShadePilot.CcdProbe.exe restore-after <baseline.json> <seconds>\n"; return 2; }
}

int main(int argc, char** argv) {
    if (argc < 3) return Usage(); const std::string command = argv[1], path = argv[2];
    CcdSnapshot s; LONG error{};
    if (command == "probe") {
        if (!Query(s, error)) { std::cerr << "QueryDisplayConfig failed: " << Hex(error) << "\n"; return 4; }
        PrintSnapshot(s, "WINDOWS CCD BASELINE"); if (!SaveBinary(path, s) || !WriteJson(path, s, "baseline")) return 5;
        std::cout << "baselineSaved=" << path << "\n"; return 0;
    }
    if (command == "validate-720p") {
        if (!Query(s, error)) { std::cerr << "QueryDisplayConfig failed: " << Hex(error) << "\n"; return 4; }
        PrintSnapshot(s, "WINDOWS CCD BEFORE VALIDATE");
        const LONG gate0 = ValidateRoundTrip(s);
        std::cout << "Gate0 SetDisplayConfig(SDC_VALIDATE, original CCD) return=" << Hex(gate0) << "\n";
        WriteJson(path + ".gate0.json", s, "gate0-roundtrip", gate0, gate0);
        if (gate0 != ERROR_SUCCESS) {
            std::cerr << "Gate0 failed; candidate modification is not attempted\n";
            WriteJson(path, s, "validate-720p", gate0, gate0);
            return 9;
        }
        std::cout << "Gate0 passed; constructing virtual-mode candidate with synchronized SOURCE, DESKTOP_IMAGE and path scaling\n";
        CcdSnapshot candidate = s;
        int candidatePath = -1;
        for (UINT32 i = 0; i < candidate.pathCount; ++i) if ((candidate.paths[i].flags & DISPLAYCONFIG_PATH_ACTIVE) && candidate.paths[i].targetInfo.outputTechnology == DISPLAYCONFIG_OUTPUT_TECHNOLOGY_HDMI) { candidatePath = static_cast<int>(i); break; }
        if (candidatePath < 0) { std::cerr << "No active HDMI path found\n"; WriteJson(path, candidate, "validate-720p", ERROR_NOT_FOUND, gate0); return 7; }
        auto& p = candidate.paths[static_cast<size_t>(candidatePath)]; const UINT32 si = SourceIndex(p); const UINT32 ti = TargetIndex(p); const UINT32 di = DesktopIndex(p);
        if (si >= candidate.modeCount || ti >= candidate.modeCount || candidate.modes[si].infoType != DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE || candidate.modes[ti].infoType != DISPLAYCONFIG_MODE_INFO_TYPE_TARGET) { std::cerr << "Mode indexes invalid for candidate path\n"; WriteJson(path, candidate, "validate-720p", ERROR_INVALID_INDEX, gate0); return 8; }
        if ((p.flags & DISPLAYCONFIG_PATH_SUPPORT_VIRTUAL_MODE) && (di >= candidate.modeCount || candidate.modes[di].infoType != DISPLAYCONFIG_MODE_INFO_TYPE_DESKTOP_IMAGE)) { std::cerr << "Virtual desktop mode index invalid for candidate path\n"; WriteJson(path, candidate, "validate-720p", ERROR_INVALID_INDEX, gate0); return 8; }
        const UINT32 oldW = candidate.modes[si].sourceMode.width, oldH = candidate.modes[si].sourceMode.height;
        candidate.modes[si].sourceMode.width = 1280; candidate.modes[si].sourceMode.height = 720;
        if (p.flags & DISPLAYCONFIG_PATH_SUPPORT_VIRTUAL_MODE) {
            auto& desktop = candidate.modes[di].desktopImageInfo;
            desktop.PathSourceSize.x = 1280; desktop.PathSourceSize.y = 720;
            desktop.DesktopImageRegion.right = desktop.DesktopImageRegion.left + 1280;
            desktop.DesktopImageRegion.bottom = desktop.DesktopImageRegion.top + 720;
            desktop.DesktopImageClip.right = desktop.DesktopImageClip.left + 1280;
            desktop.DesktopImageClip.bottom = desktop.DesktopImageClip.top + 720;
        }
        p.targetInfo.scaling = DISPLAYCONFIG_SCALING_STRETCHED;
        std::cout << "Candidate Path=" << candidatePath << " Source " << oldW << "x" << oldH << " -> 1280x720; DesktopImage synchronized; PathScaling=STRETCHED/3; Target remains " << candidate.modes[ti].targetMode.targetVideoSignalInfo.activeSize.cx << "x" << candidate.modes[ti].targetMode.targetVideoSignalInfo.activeSize.cy << "\n";
        PrintSnapshot(candidate, "WINDOWS CCD VIRTUAL-MODE CANDIDATE");
        error = SetDisplayConfig(candidate.pathCount, candidate.paths.data(), candidate.modeCount, candidate.modes.data(), SDC_VALIDATE | SDC_USE_SUPPLIED_DISPLAY_CONFIG | SDC_VIRTUAL_MODE_AWARE | SDC_VIRTUAL_REFRESH_RATE_AWARE);
        std::cout << "SetDisplayConfig(VALIDATE) return=" << Hex(error) << "\n";
        WriteJson(path, candidate, "validate-720p", error, gate0); return error == ERROR_SUCCESS ? 0 : 9;
    }
    if (command == "apply-720p") {
        const std::string baselinePath = path;
        CcdSnapshot baseline;
        if (!LoadBinary(baselinePath, baseline)) { std::cerr << "Baseline sidecar missing or unreadable: " << baselinePath << ".ccd.bin\n"; return 5; }
        CcdSnapshot current;
        if (!Query(current, error)) { std::cerr << "Current CCD query failed: " << Hex(error) << "\n"; return 4; }
        PrintSnapshot(current, "APPLY PREFLIGHT CURRENT CCD");
        if (!SameConfig(current, baseline)) { std::cerr << "Current CCD does not match baseline; refusing APPLY\n"; return 6; }
        const LONG preflightGate0 = ValidateRoundTrip(current);
        std::cout << "APPLY preflight Gate0 return=" << Hex(preflightGate0) << "\n";
        if (preflightGate0 != ERROR_SUCCESS) { std::cerr << "APPLY preflight Gate0 failed; refusing APPLY\n"; return 6; }
        CcdSnapshot candidate; int candidatePath = -1; std::string reason;
        if (!Build720pCandidate(current, candidate, candidatePath, reason)) { std::cerr << reason << "\n"; return 7; }
        WriteJson(baselinePath + ".apply-candidate.json", candidate, "apply-candidate", ERROR_SUCCESS, preflightGate0);
        if (!StartRestoreWatchdog(baselinePath, 15)) { std::cerr << "Could not start 15-second restore watchdog; refusing APPLY\n"; return 6; }
        std::cout << "Restore watchdog started: 15 seconds\n";
        const LONG applyResult = ApplyConfig(candidate, false);
        std::cout << "SetDisplayConfig(APPLY) return=" << Hex(applyResult) << "\n";
        if (applyResult != ERROR_SUCCESS) {
            const LONG restoreResult = RestoreConfig(baseline);
            std::cout << "Immediate restore after APPLY failure return=" << Hex(restoreResult) << "\n";
            return 10;
        }
        CcdSnapshot actual; LONG queryAfterApplyError{}; bool gate1 = false;
        if (Query(actual, queryAfterApplyError)) {
            PrintSnapshot(actual, "WINDOWS CCD AFTER APPLY");
            int actualPath = -1; for (UINT32 i = 0; i < actual.pathCount; ++i) if ((actual.paths[i].flags & DISPLAYCONFIG_PATH_ACTIVE) && actual.paths[i].targetInfo.outputTechnology == DISPLAYCONFIG_OUTPUT_TECHNOLOGY_HDMI) { actualPath = static_cast<int>(i); break; }
            gate1 = IsTarget720p(actual, actualPath);
            WriteJson(baselinePath + ".after-apply.json", actual, "after-apply", applyResult, preflightGate0);
        } else {
            std::cerr << "QueryDisplayConfig after APPLY failed: " << Hex(queryAfterApplyError) << "\n";
        }
        std::cout << "Gate1 Source=1280x720, DesktopImage=1280x720, Target=1920x1080, PathScaling=STRETCHED => " << (gate1 ? "SUCCESS" : "FAILED") << "\n";
        if (!gate1) {
            std::cout << "APPLY succeeded but Gate1 failed; attempting the single permitted SDC_NO_OPTIMIZATION retry\n";
            const LONG retryResult = ApplyConfig(candidate, true);
            std::cout << "SetDisplayConfig(APPLY|SDC_NO_OPTIMIZATION) return=" << Hex(retryResult) << "\n";
            CcdSnapshot retryActual; LONG retryQueryError{}; bool retryGate1 = false;
            if (retryResult == ERROR_SUCCESS && Query(retryActual, retryQueryError)) {
                PrintSnapshot(retryActual, "WINDOWS CCD AFTER NO_OPTIMIZATION RETRY");
                int retryPath = -1; for (UINT32 i = 0; i < retryActual.pathCount; ++i) if ((retryActual.paths[i].flags & DISPLAYCONFIG_PATH_ACTIVE) && retryActual.paths[i].targetInfo.outputTechnology == DISPLAYCONFIG_OUTPUT_TECHNOLOGY_HDMI) { retryPath = static_cast<int>(i); break; }
                retryGate1 = IsTarget720p(retryActual, retryPath);
                WriteJson(baselinePath + ".after-no-optimization.json", retryActual, "after-no-optimization", retryResult, preflightGate0);
            } else if (retryResult == ERROR_SUCCESS) {
                std::cerr << "QueryDisplayConfig after NO_OPTIMIZATION retry failed: " << Hex(retryQueryError) << "\n";
            }
            gate1 = retryGate1;
            std::cout << "Gate1 after single retry => " << (gate1 ? "SUCCESS" : "FAILED") << "\n";
        }
        if (!gate1) {
            const LONG restoreResult = RestoreConfig(baseline);
            std::cout << "Restore after Gate1 failure return=" << Hex(restoreResult) << "\n";
            return 11;
        }
        std::cout << "Gate1 passed; holding experimental mode for 15 seconds for IGCL read, then watchdog restore\n";
        std::cout.flush(); std::this_thread::sleep_for(std::chrono::seconds(15));
        CcdSnapshot restoredCheck; LONG restoredQueryError{};
        if (!Query(restoredCheck, restoredQueryError) || !SameConfig(restoredCheck, baseline)) {
            const LONG restoreResult = RestoreConfig(baseline);
            std::cout << "Post-hold restore fallback return=" << Hex(restoreResult) << "\n";
        } else {
            std::cout << "Watchdog restore verified by CCD readback\n";
        }
        return 0;
    }
    if (command == "restore-after") {
        if (argc != 4) return Usage();
        char* end = nullptr; const unsigned long seconds = std::strtoul(argv[3], &end, 10);
        if (!end || *end != '\0' || seconds > 300) return Usage();
        std::this_thread::sleep_for(std::chrono::seconds(seconds));
        CcdSnapshot baseline;
        if (!LoadBinary(path, baseline)) { std::cerr << "Watchdog baseline sidecar missing: " << path << ".ccd.bin\n"; return 5; }
        const LONG restoreResult = RestoreConfig(baseline);
        std::cout << "Watchdog restore return=" << Hex(restoreResult) << "\n";
        return restoreResult == ERROR_SUCCESS ? 0 : 10;
    }
    if (command == "restore") {
        if (!LoadBinary(path, s)) { std::cerr << "Cannot load baseline sidecar: " << path << ".ccd.bin\n"; return 5; }
        error = RestoreConfig(s);
        std::cout << "SetDisplayConfig(RESTORE) return=" << Hex(error) << "\n"; return error == ERROR_SUCCESS ? 0 : 10;
    }
    return Usage();
}
