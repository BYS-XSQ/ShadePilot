// ShadePilot DriverControl - explicit Intel IGCL Sharpness write/restore tool.
// --inspect is read-only. --set and --restore are the only paths that call ctlSetCurrentSharpness.
#include <windows.h>
#include <cmath>
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
bool ParseU32(const char* text, uint32_t& value) { char* end = nullptr; unsigned long v = std::strtoul(text, &end, 10); if (!text || *text == '\0' || !end || *end != '\0' || v > 0xFFFFFFFFul) return false; value = static_cast<uint32_t>(v); return true; }
bool ParseFloat(const char* text, float& value) { char* end = nullptr; value = std::strtof(text, &end); return text && *text && end && *end == '\0' && std::isfinite(value); }
struct OutputState { ctl_result_t capsResult{}; ctl_sharpness_caps_t caps{}; std::vector<ctl_sharpness_filter_properties_t> filters; ctl_result_t currentResult{}; ctl_sharpness_settings_t current{}; };
bool ReadOutput(ctl_display_output_handle_t output, OutputState& state) {
    state.caps = {}; state.caps.Size = sizeof(state.caps); state.capsResult = ctlGetSharpnessCaps(output, &state.caps);
    if (state.capsResult == CTL_RESULT_SUCCESS && state.caps.NumFilterTypes) { state.filters.resize(state.caps.NumFilterTypes); state.caps.pFilterProperty = state.filters.data(); state.capsResult = ctlGetSharpnessCaps(output, &state.caps); }
    state.current = {}; state.current.Size = sizeof(state.current); state.currentResult = ctlGetCurrentSharpness(output, &state.current);
    return state.capsResult == CTL_RESULT_SUCCESS && state.currentResult == CTL_RESULT_SUCCESS;
}
void PrintOutput(uint32_t index, const OutputState& state) {
    std::cout << "{\"output\": " << index << ", \"capsResult\": " << Json(Hex(state.capsResult)) << ", \"supportedFilterFlags\": " << state.caps.SupportedFilterFlags << ", \"filters\": [";
    for (size_t i = 0; i < state.filters.size(); ++i) { if (i) std::cout << ", "; const auto& f = state.filters[i]; std::cout << "{\"type\": " << f.FilterType << ", \"min\": " << f.FilterDetails.min_possible_value << ", \"max\": " << f.FilterDetails.max_possible_value << ", \"step\": " << f.FilterDetails.step_size << "}"; }
    std::cout << "], \"currentResult\": " << Json(Hex(state.currentResult)) << ", \"current\": {\"enabled\": " << (state.current.Enable ? "true" : "false") << ", \"filterType\": " << state.current.FilterType << ", \"intensity\": " << state.current.Intensity << "}}\n";
}
bool Validate(const OutputState& state, uint32_t filterType, float intensity) {
    for (const auto& f : state.filters) { if (f.FilterType != filterType) continue; const float step = f.FilterDetails.step_size; const float relative = step > 0 ? (intensity - f.FilterDetails.min_possible_value) / step : 0.0f; return intensity >= f.FilterDetails.min_possible_value && intensity <= f.FilterDetails.max_possible_value && (step <= 0 || std::fabs(relative - std::round(relative)) < 0.0001f); }
    return false;
}
bool WriteBackup(const std::string& path, uint32_t output, const ctl_sharpness_settings_t& current) {
    std::ofstream file(path, std::ios::trunc); if (!file) return false;
    file << "output=" << output << "\n" << "enabled=" << (current.Enable ? 1 : 0) << "\n" << "filterType=" << current.FilterType << "\n" << "intensity=" << std::setprecision(9) << current.Intensity << "\n";
    return true;
}
bool ReadBackup(const std::string& path, uint32_t& output, bool& enabled, uint32_t& filterType, float& intensity) {
    std::ifstream file(path); if (!file) return false; std::string line;
    while (std::getline(file, line)) { const auto split = line.find('='); if (split == std::string::npos) continue; const std::string key = line.substr(0, split), value = line.substr(split + 1);
        if (key == "output") { if (!ParseU32(value.c_str(), output)) return false; } else if (key == "enabled") { uint32_t v{}; if (!ParseU32(value.c_str(), v) || v > 1) return false; enabled = v != 0; } else if (key == "filterType") { if (!ParseU32(value.c_str(), filterType)) return false; } else if (key == "intensity") { if (!ParseFloat(value.c_str(), intensity)) return false; }
    }
    return true;
}
int Usage() { std::cerr << "Usage:\n  ShadePilot.DriverControl.exe --inspect\n  ShadePilot.DriverControl.exe --set <output> <enabled:0|1> <filterType> <intensity> [backup.txt]\n  ShadePilot.DriverControl.exe --restore <backup.txt>\n"; return 2; }
}

int main(int argc, char** argv) {
    const bool implicitInspect = argc < 2;
    const std::string command = implicitInspect ? "--inspect" : argv[1];
    if (command != "--inspect" && command != "--set" && command != "--restore") return Usage();
    ctl_init_args_t init{}; init.Size = sizeof(init); init.Version = 0; init.AppVersion = CTL_MAKE_VERSION(CTL_IMPL_MAJOR_VERSION, CTL_IMPL_MINOR_VERSION); init.flags = 0;
    ctl_api_handle_t api = nullptr; const ctl_result_t initResult = ctlInit(&init, &api);
    if (initResult != CTL_RESULT_SUCCESS || !api) { std::cerr << "{\"initResult\": " << Json(Hex(initResult)) << ", \"status\": \"Driver Enhancement Unsupported\"}\n"; return 0; }
    uint32_t adapterCount = 0; ctl_result_t result = ctlEnumerateDevices(api, &adapterCount, nullptr);
    if (result != CTL_RESULT_SUCCESS || adapterCount == 0) { ctlClose(api); std::cerr << "No Intel adapter; result=" << Hex(result) << "\n"; return 0; }
    std::vector<ctl_device_adapter_handle_t> adapters(adapterCount); result = ctlEnumerateDevices(api, &adapterCount, adapters.data());
    if (result != CTL_RESULT_SUCCESS) { ctlClose(api); std::cerr << "Adapter enumeration failed: " << Hex(result) << "\n"; return 0; }
    uint32_t outputCount = 0; result = ctlEnumerateDisplayOutputs(adapters[0], &outputCount, nullptr);
    if (result != CTL_RESULT_SUCCESS || outputCount == 0) { ctlClose(api); std::cerr << "No display output; result=" << Hex(result) << "\n"; return 0; }
    std::vector<ctl_display_output_handle_t> outputs(outputCount); result = ctlEnumerateDisplayOutputs(adapters[0], &outputCount, outputs.data());
    if (result != CTL_RESULT_SUCCESS) { ctlClose(api); std::cerr << "Display enumeration failed: " << Hex(result) << "\n"; return 0; }
    if (command == "--inspect") {
        std::cout << "{\"mode\": \"read-only\", \"outputCount\": " << outputCount << ", \"outputs\": [\n";
        for (uint32_t i = 0; i < outputCount; ++i) { if (i) std::cout << ",\n"; OutputState state; ReadOutput(outputs[i], state); PrintOutput(i, state); }
        std::cout << "]}\n";
        if (implicitInspect && GetConsoleWindow() != nullptr) { std::cout << "\nRead-only inspection complete. Press any key to close.\n"; std::system("pause > nul"); }
        ctlClose(api); return 0;
    }
    uint32_t outputIndex = 0, filterType = 0; bool enabled = false; float intensity = 0.0f; std::string backupPath;
    if (command == "--set") {
        uint32_t enabledValue = 0;
        if (argc < 6 || !ParseU32(argv[2], outputIndex) || !ParseU32(argv[3], enabledValue) || enabledValue > 1 || !ParseU32(argv[4], filterType) || !ParseFloat(argv[5], intensity)) return Usage();
        enabled = enabledValue != 0; backupPath = argc >= 7 ? argv[6] : "sharpness-backup.txt";
    } else {
        if (argc != 3) return Usage(); backupPath = argv[2];
        bool restoredEnabled = false; if (!ReadBackup(backupPath, outputIndex, restoredEnabled, filterType, intensity)) { std::cerr << "Invalid backup: " << backupPath << "\n"; ctlClose(api); return 3; } enabled = restoredEnabled;
    }
    if (outputIndex >= outputCount) { std::cerr << "Output index out of range: " << outputIndex << "\n"; ctlClose(api); return 3; }
    OutputState state; if (!ReadOutput(outputs[outputIndex], state)) { std::cerr << "Sharpness read failed: caps=" << Hex(state.capsResult) << ", current=" << Hex(state.currentResult) << "\n"; ctlClose(api); return 4; }
    if (!Validate(state, filterType, intensity)) { std::cerr << "Requested filter/intensity is outside the reported IGCL range.\n"; ctlClose(api); return 3; }
    if (command == "--set" && !WriteBackup(backupPath, outputIndex, state.current)) { std::cerr << "Cannot write backup: " << backupPath << "\n"; ctlClose(api); return 5; }
    ctl_sharpness_settings_t next = state.current; next.Enable = enabled; next.FilterType = filterType; next.Intensity = intensity;
    const ctl_result_t setResult = ctlSetCurrentSharpness(outputs[outputIndex], &next); OutputState after; ReadOutput(outputs[outputIndex], after);
    std::cout << "{\"mode\": " << Json(command) << ", \"setResult\": " << Json(Hex(setResult)) << ", \"readbackResult\": " << Json(Hex(after.currentResult)) << ", \"readback\": {\"enabled\": " << (after.current.Enable ? "true" : "false") << ", \"filterType\": " << after.current.FilterType << ", \"intensity\": " << after.current.Intensity << "}, \"backup\": " << Json(backupPath) << "}\n";
    ctlClose(api); return setResult == CTL_RESULT_SUCCESS && after.currentResult == CTL_RESULT_SUCCESS && after.current.Enable == enabled && after.current.FilterType == filterType && std::fabs(after.current.Intensity - intensity) < 0.0001f ? 0 : 6;
}
