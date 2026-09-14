#include <windows.h>
#include <cstdlib>
#include <fstream>
#include <iomanip>
#include <iostream>
#include <sstream>
#include <string>
#include <vector>
#include "igcl_api.h"

namespace {
std::string Hex(ctl_result_t r) { std::ostringstream o; o << "0x" << std::uppercase << std::hex << static_cast<uint32_t>(r); return o.str(); }
std::string Json(const std::string& s) { std::ostringstream o; o << '"'; for (unsigned char c : s) { if (c == '\\') o << "\\\\"; else if (c == '"') o << "\\\""; else o << static_cast<char>(c); } o << '"'; return o.str(); }
bool ParseU32(const char* text, uint32_t& value) { if (!text || !*text) return false; char* end = nullptr; unsigned long v = std::strtoul(text, &end, 10); if (!end || *end || v > 0xFFFFFFFFul) return false; value = static_cast<uint32_t>(v); return true; }
struct Snapshot {
    ctl_result_t displayResult{};
    ctl_display_properties_t display{};
    ctl_result_t capsResult{};
    ctl_scaling_caps_t caps{};
    ctl_result_t currentResult{};
    ctl_scaling_settings_t current{};
};
void PrintEnums() {
    std::cout << "Identity=" << CTL_SCALING_TYPE_FLAG_IDENTITY
              << " Centered=" << CTL_SCALING_TYPE_FLAG_CENTERED
              << " Stretched=" << CTL_SCALING_TYPE_FLAG_STRETCHED
              << " AspectRatioCenteredMax=" << CTL_SCALING_TYPE_FLAG_ASPECT_RATIO_CENTERED_MAX
              << " Custom=" << CTL_SCALING_TYPE_FLAG_CUSTOM << "\n";
}
bool ReadSnapshot(ctl_display_output_handle_t output, Snapshot& s) {
    s.display = {}; s.display.Size = sizeof(s.display); s.displayResult = ctlGetDisplayProperties(output, &s.display);
    s.caps = {}; s.caps.Size = sizeof(s.caps); s.capsResult = ctlGetSupportedScalingCapability(output, &s.caps);
    s.current = {}; s.current.Size = sizeof(s.current); s.current.Version = 1; s.currentResult = ctlGetCurrentScaling(output, &s.current);
    return s.capsResult == CTL_RESULT_SUCCESS && s.currentResult == CTL_RESULT_SUCCESS;
}
void PrintSnapshot(uint32_t index, const Snapshot& s) {
    std::cout << "{";
    std::cout << "\"output\":" << index
              << ",\"displayResult\":" << Json(Hex(s.displayResult))
              << ",\"displayType\":" << s.display.Type
              << ",\"displayConfigFlags\":" << s.display.DisplayConfigFlags
              << ",\"timingHActive\":" << s.display.Display_Timing_Info.HActive
              << ",\"timingVActive\":" << s.display.Display_Timing_Info.VActive
              << ",\"timingRefreshRate\":" << s.display.Display_Timing_Info.RefreshRate
              << ",\"scalingCapsResult\":" << Json(Hex(s.capsResult))
              << ",\"supportedScaling\":" << s.caps.SupportedScaling
              << ",\"scalingResult\":" << Json(Hex(s.currentResult))
              << ",\"scaling\":{\"enabled\":" << (s.current.Enable ? "true" : "false")
              << ",\"type\":" << s.current.ScalingType
              << ",\"preferredType\":" << s.current.PreferredScalingType
              << ",\"customX\":" << s.current.CustomScalingX
              << ",\"customY\":" << s.current.CustomScalingY
              << ",\"hardwareModeSet\":" << (s.current.HardwareModeSet ? "true" : "false") << "}}\n";
}
bool WriteBaseline(const std::string& path, uint32_t output, const Snapshot& s) {
    std::ofstream f(path, std::ios::trunc); if (!f) return false;
    f << "{\n  \"output\": " << output << ",\n"
      << "  \"displayType\": " << s.display.Type << ",\n"
      << "  \"displayConfigFlags\": " << s.display.DisplayConfigFlags << ",\n"
      << "  \"timingHActive\": " << s.display.Display_Timing_Info.HActive << ",\n"
      << "  \"timingVActive\": " << s.display.Display_Timing_Info.VActive << ",\n"
      << "  \"timingRefreshRate\": " << s.display.Display_Timing_Info.RefreshRate << ",\n"
      << "  \"supportedScaling\": " << s.caps.SupportedScaling << ",\n"
      << "  \"scalingResult\": " << Json(Hex(s.currentResult)) << ",\n"
      << "  \"scaling\": {\"enabled\": " << (s.current.Enable ? "true" : "false")
      << ", \"type\": " << s.current.ScalingType
      << ", \"preferredType\": " << s.current.PreferredScalingType
      << ", \"customX\": " << s.current.CustomScalingX
      << ", \"customY\": " << s.current.CustomScalingY
      << ", \"hardwareModeSet\": " << (s.current.HardwareModeSet ? "true" : "false") << "}\n}\n";
    return true;
}
int Usage() {
    std::cerr << "Usage:\n"
              << "  ShadePilot.ScalingValidation.exe --inspect <output> [baseline.json]\n"
              << "  ShadePilot.ScalingValidation.exe --set-stretched <output> <baseline.json>\n"
              << "  ShadePilot.ScalingValidation.exe --restore <output> <enabled:0|1> <type> <preferredType> <customX> <customY> <hardwareModeSet:0|1>\n";
    return 2;
}
}

int main(int argc, char** argv) {
    if (argc < 3) return Usage();
    const std::string command = argv[1];
    uint32_t outputIndex = 0; if (!ParseU32(argv[2], outputIndex)) return Usage();
    ctl_init_args_t init{}; init.Size = sizeof(init); init.Version = 0; init.AppVersion = CTL_MAKE_VERSION(CTL_IMPL_MAJOR_VERSION, CTL_IMPL_MINOR_VERSION);
    ctl_api_handle_t api = nullptr; ctl_result_t initResult = ctlInit(&init, &api);
    if (initResult != CTL_RESULT_SUCCESS || !api) { std::cerr << "{\"initResult\":" << Json(Hex(initResult)) << "}\n"; return 4; }
    uint32_t adapterCount = 0; ctl_result_t r = ctlEnumerateDevices(api, &adapterCount, nullptr);
    if (r != CTL_RESULT_SUCCESS || adapterCount == 0) { ctlClose(api); std::cerr << "No Intel adapter: " << Hex(r) << "\n"; return 4; }
    std::vector<ctl_device_adapter_handle_t> adapters(adapterCount); r = ctlEnumerateDevices(api, &adapterCount, adapters.data());
    uint32_t outputCount = 0; r = ctlEnumerateDisplayOutputs(adapters[0], &outputCount, nullptr);
    if (r != CTL_RESULT_SUCCESS || outputIndex >= outputCount) { ctlClose(api); std::cerr << "Output unavailable: " << Hex(r) << "\n"; return 4; }
    std::vector<ctl_display_output_handle_t> outputs(outputCount); r = ctlEnumerateDisplayOutputs(adapters[0], &outputCount, outputs.data());
    if (r != CTL_RESULT_SUCCESS) { ctlClose(api); return 4; }
    PrintEnums();
    Snapshot before; if (!ReadSnapshot(outputs[outputIndex], before)) { PrintSnapshot(outputIndex, before); ctlClose(api); return 5; }
    if (command == "--inspect") {
        PrintSnapshot(outputIndex, before);
        if (argc >= 4 && !WriteBaseline(argv[3], outputIndex, before)) { std::cerr << "Cannot write baseline\n"; ctlClose(api); return 6; }
        ctlClose(api); return 0;
    }
    if (command == "--set-stretched") {
        if (argc != 4) return Usage();
        const std::string baselinePath = argv[3];
        if (!WriteBaseline(baselinePath, outputIndex, before)) { std::cerr << "Cannot write baseline\n"; ctlClose(api); return 6; }
        const uint32_t stretched = CTL_SCALING_TYPE_FLAG_STRETCHED;
        if ((before.caps.SupportedScaling & stretched) == 0) {
            std::cerr << "{\"status\":\"Stretched unsupported\",\"supportedScaling\":" << before.caps.SupportedScaling << "}\n";
            ctlClose(api); return 7;
        }
        ctl_scaling_settings_t next = before.current; next.Enable = true; next.ScalingType = stretched; next.HardwareModeSet = true;
        ctl_result_t setResult = ctlSetCurrentScaling(outputs[outputIndex], &next);
        Snapshot after; ReadSnapshot(outputs[outputIndex], after);
        PrintSnapshot(outputIndex, after);
        const bool reached = setResult == CTL_RESULT_SUCCESS && after.currentResult == CTL_RESULT_SUCCESS && after.current.Enable && after.current.ScalingType == stretched;
        std::cout << "{\"setResult\":" << Json(Hex(setResult)) << ",\"reachedStretched\":" << (reached ? "true" : "false") << "}\n";
        ctlClose(api); return reached ? 0 : 8;
    }
    if (command == "--restore") {
        if (argc != 8 && argc != 9) return Usage();
        uint32_t enabled=0, type=0, preferred=0, customX=0, customY=0, hardware=0;
        if (!ParseU32(argv[3], enabled) || !ParseU32(argv[4], type) || enabled > 1) return Usage();
        if (argc == 9) {
            if (!ParseU32(argv[5], preferred) || !ParseU32(argv[6], customX) || !ParseU32(argv[7], customY) || !ParseU32(argv[8], hardware)) return Usage();
        } else {
            if (!ParseU32(argv[5], customX) || !ParseU32(argv[6], customY) || !ParseU32(argv[7], hardware)) return Usage();
            preferred = type;
        }
        if (hardware > 1) return Usage();
        ctl_scaling_settings_t restore = before.current; restore.Enable = enabled != 0; restore.ScalingType = type; restore.PreferredScalingType = preferred; restore.CustomScalingX = customX; restore.CustomScalingY = customY; restore.HardwareModeSet = hardware != 0;
        ctl_result_t setResult = ctlSetCurrentScaling(outputs[outputIndex], &restore); Snapshot after; ReadSnapshot(outputs[outputIndex], after); PrintSnapshot(outputIndex, after);
        const bool ok = setResult == CTL_RESULT_SUCCESS && after.currentResult == CTL_RESULT_SUCCESS && after.current.Enable == restore.Enable && after.current.ScalingType == restore.ScalingType;
        std::cout << "{\"restoreResult\":" << Json(Hex(setResult)) << ",\"restored\":" << (ok ? "true" : "false") << "}\n";
        ctlClose(api); return ok ? 0 : 9;
    }
    ctlClose(api); return Usage();
}
