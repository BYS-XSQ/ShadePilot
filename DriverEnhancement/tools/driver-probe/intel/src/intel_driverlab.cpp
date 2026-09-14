// ShadePilot.DriverLab - Intel IGCL read-only capability probe.
// This executable intentionally contains no IGCL Set call and no game interaction.
#include <ctime>
#include <filesystem>
#include <fstream>
#include <iomanip>
#include <iostream>
#include <map>
#include <sstream>
#include <string>
#include <vector>
#include "igcl_api.h"

namespace {
namespace fs = std::filesystem;

struct FeaturePlan { ctl_3d_feature_t type; const char* id; const char* name; };
const FeaturePlan kFeatures[] = {
    {CTL_3D_FEATURE_FRAME_LIMIT, "frame_limit", "Frame Limit"},
    {CTL_3D_FEATURE_LOW_LATENCY, "low_latency", "Low Latency"},
    {CTL_3D_FEATURE_TEXTURE_FILTERING_QUALITY, "texture_filtering_quality", "Texture Filtering Quality"},
    {CTL_3D_FEATURE_CMAA, "cmaa", "CMAA"},
    {CTL_3D_FEATURE_MSAA, "msaa", "MSAA"},
    {CTL_3D_FEATURE_GAMING_FLIP_MODES, "gaming_flip_modes", "Gaming Flip Modes"},
    {CTL_3D_FEATURE_ADAPTIVE_SYNC_PLUS, "adaptive_sync_plus", "Adaptive Sync Plus"},
    {CTL_3D_FEATURE_APP_PROFILES, "app_profiles", "App Profiles"},
    {CTL_3D_FEATURE_GLOBAL_OR_PER_APP, "global_or_per_app", "Global Or Per-App"},
    {CTL_3D_FEATURE_LIVE_STATE, "live_state", "Live State"},
    {CTL_3D_FEATURE_FRAME_GENERATION, "frame_generation", "Frame Generation"},
    {CTL_3D_FEATURE_FRAME_GENERATION_CONTROL, "frame_generation_control", "Frame Generation Control"},
    {CTL_3D_FEATURE_SHARPENING_FILTER, "sharpening_filter", "Sharpening Filter"},
    {CTL_3D_FEATURE_ANISOTROPIC, "anisotropic_filtering", "Anisotropic Filtering"},
    {CTL_3D_FEATURE_ADAPTIVE_TESSELLATION, "adaptive_tessellation", "Adaptive Tessellation"},
    {CTL_3D_FEATURE_FRAME_PACING, "frame_pacing", "Frame Pacing"},
    {CTL_3D_FEATURE_VRR_WINDOWED_BLT, "vrr_windowed_blt", "VRR Windowed BLT"},
    {CTL_3D_FEATURE_PREBUILT_SHADER_DOWNLOAD, "prebuilt_shader_download", "Prebuilt Shader Download"},
};

std::string Json(const std::string& text) { std::ostringstream out; out << '"'; for (const unsigned char c : text) { if (c == '\\') out << "\\\\"; else if (c == '"') out << "\\\""; else if (c == '\n') out << "\\n"; else if (c == '\r') out << "\\r"; else if (c == '\t') out << "\\t"; else if (c < 0x20) out << '?'; else out << static_cast<char>(c); } return out << '"', out.str(); }
std::string Hex(const ctl_result_t value) { std::ostringstream out; out << "0x" << std::uppercase << std::hex << static_cast<uint32_t>(value); return out.str(); }
std::string Now() { const auto now = std::time(nullptr); std::tm local{}; localtime_s(&local, &now); std::ostringstream out; out << std::put_time(&local, "%Y-%m-%d %H:%M:%S"); return out.str(); }
std::string ValueTypeName(const ctl_property_value_type_t value) { switch (value) { case CTL_PROPERTY_VALUE_TYPE_BOOL:return "Bool"; case CTL_PROPERTY_VALUE_TYPE_FLOAT:return "Float"; case CTL_PROPERTY_VALUE_TYPE_INT32:return "Int32"; case CTL_PROPERTY_VALUE_TYPE_UINT32:return "UInt32"; case CTL_PROPERTY_VALUE_TYPE_ENUM:return "Enum"; case CTL_PROPERTY_VALUE_TYPE_CUSTOM:return "Custom"; default:return "Unknown"; } }
bool IsArchived(const ctl_3d_feature_t type) { return type == CTL_3D_FEATURE_SHARPENING_FILTER || type == CTL_3D_FEATURE_ANISOTROPIC || type == CTL_3D_FEATURE_ADAPTIVE_TESSELLATION; }
std::string ArchivedReason(const ctl_3d_feature_t type) { if (type == CTL_3D_FEATURE_SHARPENING_FILTER) return "Archived: Intel Display Output Sharpness accepted Set/Get but showed no visible 1/3/5 difference on this B580 test path; not a shipping candidate."; if (type == CTL_3D_FEATURE_ANISOTROPIC) return "Archived by product policy: Intel Graphics Software removed this control from the current route; no Set/A-B test is allowed."; return "Archived by product policy: Intel Graphics Software removed Adaptive Tessellation from the current route; no Set/A-B test is allowed."; }

std::string CapabilityValues(const ctl_3d_feature_details_t& detail) {
    std::ostringstream out;
    switch (detail.ValueType) {
        case CTL_PROPERTY_VALUE_TYPE_BOOL: out << "default=" << (detail.Value.BoolType.DefaultState ? "true" : "false"); break;
        case CTL_PROPERTY_VALUE_TYPE_FLOAT: { const auto& r = detail.Value.FloatType.RangeInfo; out << "min=" << r.min_possible_value << ", max=" << r.max_possible_value << ", step=" << r.step_size << ", default=" << r.default_value; break; }
        case CTL_PROPERTY_VALUE_TYPE_INT32: { const auto& r = detail.Value.IntType.RangeInfo; out << "min=" << r.min_possible_value << ", max=" << r.max_possible_value << ", step=" << r.step_size << ", default=" << r.default_value; break; }
        case CTL_PROPERTY_VALUE_TYPE_UINT32: { const auto& r = detail.Value.UIntType.RangeInfo; out << "min=" << r.min_possible_value << ", max=" << r.max_possible_value << ", step=" << r.step_size << ", default=" << r.default_value; break; }
        case CTL_PROPERTY_VALUE_TYPE_ENUM: out << "supportedMask=" << detail.Value.EnumType.SupportedTypes << ", default=" << detail.Value.EnumType.DefaultType; break;
        case CTL_PROPERTY_VALUE_TYPE_CUSTOM: out << "Not exposed safely by current DriverLab: custom value size=" << detail.CustomValueSize; break;
        default: out << "Not exposed by current IGCL version"; break;
    }
    return out.str();
}
std::string CurrentValue(const ctl_property_value_type_t type, const ctl_property_t& value) { std::ostringstream out; switch (type) { case CTL_PROPERTY_VALUE_TYPE_BOOL:out << "enabled=" << (value.BoolType.Enable ? "true" : "false");break; case CTL_PROPERTY_VALUE_TYPE_FLOAT:out << "enabled=" << (value.FloatType.Enable ? "true" : "false") << ", value=" << value.FloatType.Value;break; case CTL_PROPERTY_VALUE_TYPE_INT32:out << "enabled=" << (value.IntType.Enable ? "true" : "false") << ", value=" << value.IntType.Value;break; case CTL_PROPERTY_VALUE_TYPE_UINT32:out << "enabled=" << (value.UIntType.Enable ? "true" : "false") << ", value=" << value.UIntType.Value;break; case CTL_PROPERTY_VALUE_TYPE_ENUM:out << "value=" << value.EnumType.EnableType;break; default:out << "Not exposed by current IGCL version";break;} return out.str(); }

struct FeatureResult { FeaturePlan plan{}; bool capabilityReported{}; ctl_3d_feature_details_t detail{}; ctl_result_t readResult{CTL_RESULT_ERROR_NOT_AVAILABLE}; std::string values, current, availability, verification, reason; };

FeatureResult ProbeFeature(const FeaturePlan& plan, const std::map<int, ctl_3d_feature_details_t>& details, ctl_device_adapter_handle_t adapter) {
    FeatureResult result{}; result.plan = plan; const auto found = details.find(static_cast<int>(plan.type));
    if (found == details.end()) { result.availability = plan.type == CTL_3D_FEATURE_SHARPENING_FILTER ? "KnownIneffective" : IsArchived(plan.type) ? "KnownRemoved" : "Unsupported"; result.verification = plan.type == CTL_3D_FEATURE_SHARPENING_FILTER ? "KnownIneffective" : IsArchived(plan.type) ? "KnownRemoved" : "None"; result.reason = IsArchived(plan.type) ? ArchivedReason(plan.type) : "Not returned by ctlGetSupported3DCapabilities on this adapter."; result.values = "Not exposed by current IGCL version"; result.current = "Not queried"; return result; }
    result.capabilityReported = true; result.detail = found->second; result.values = CapabilityValues(result.detail);
    if (IsArchived(plan.type)) { result.availability = plan.type == CTL_3D_FEATURE_SHARPENING_FILTER ? "KnownIneffective" : "KnownRemoved"; result.verification = result.availability; result.reason = ArchivedReason(plan.type); }
    else { result.availability = "Supported"; result.verification = "ApiExposed"; result.reason = "Reported by ctlGetSupported3DCapabilities; probe only, no Set call."; }
    if (result.detail.ValueType == CTL_PROPERTY_VALUE_TYPE_CUSTOM) { result.readResult = CTL_RESULT_ERROR_UNSUPPORTED_FEATURE; result.current = "Not queried: custom payload is not safely modelled by current DriverLab."; return result; }
    ctl_3d_feature_getset_t read{}; read.Size = sizeof(read); read.Version = 0; read.FeatureType = plan.type; read.bSet = false; read.ValueType = result.detail.ValueType; read.ApplicationName = nullptr; read.ApplicationNameLength = 0; read.CustomValueSize = 0; read.pCustomValue = nullptr;
    result.readResult = ctlGetSet3DFeature(adapter, &read);
    if (result.readResult == CTL_RESULT_SUCCESS) { result.current = CurrentValue(result.detail.ValueType, read.Value); if (!IsArchived(plan.type)) result.verification = "ReadVerified"; }
    else result.current = "Not read: " + Hex(result.readResult);
    return result;
}

void WriteFeatureJson(std::ostream& out, const FeatureResult& result) {
    out << "        {\n" << "          \"featureId\": " << Json(result.plan.id) << ",\n" << "          \"name\": " << Json(result.plan.name) << ",\n" << "          \"apiExposed\": " << (result.capabilityReported ? "true" : "false") << ",\n" << "          \"capabilityReported\": " << (result.capabilityReported ? "true" : "false") << ",\n" << "          \"availability\": " << Json(result.availability) << ",\n" << "          \"valueType\": " << Json(result.capabilityReported ? ValueTypeName(result.detail.ValueType) : "Not exposed by current IGCL version") << ",\n" << "          \"values\": " << Json(result.values) << ",\n" << "          \"currentValueResult\": " << Json(result.capabilityReported && result.detail.ValueType != CTL_PROPERTY_VALUE_TYPE_CUSTOM ? Hex(result.readResult) : "Not queried") << ",\n" << "          \"currentValue\": " << Json(result.current) << ",\n" << "          \"globalSupported\": " << (result.capabilityReported ? "true" : "false") << ",\n" << "          \"perAppSupported\": " << (result.capabilityReported && result.detail.PerAppSupport ? "true" : "false") << ",\n" << "          \"liveChangeSupported\": " << (result.capabilityReported && (result.detail.FeatureMiscSupport & CTL_3D_FEATURE_MISC_FLAG_LIVE_CHANGE) ? "true" : "false") << ",\n" << "          \"scope\": " << Json(!result.capabilityReported ? "Unknown" : result.detail.PerAppSupport ? "GlobalGPU or PerApplication" : "GlobalGPU") << ",\n" << "          \"verificationLevel\": " << Json(result.verification) << ",\n" << "          \"reason\": " << Json(result.reason) << "\n        }";
}

void WriteMarkdown(const fs::path& path, const std::string& timestamp, const ctl_init_args_t& init, const std::string& initResult, const std::string& adapterName, uint32_t deviceId, uint64_t driverVersion, const std::vector<FeatureResult>& features) {
    std::ofstream out(path, std::ios::trunc); out << "# ShadePilot Intel 3D Capability Report\n\n- Mode: read-only `probe`\n- Timestamp: " << timestamp << "\n- IGCL init: " << initResult << "\n- IGCL implementation version: " << init.SupportedVersion << "\n- Adapter: " << adapterName << "\n- Device ID: " << deviceId << "\n- Driver version raw: " << driverVersion << "\n\nNo `ctlSetCurrent*` or `ctlGetSet3DFeature(... bSet=true)` call exists in this executable.\n\n| Feature | Reported | Availability | Current read | Per-app | Live | Verification |\n|---|---:|---|---|---:|---:|---|\n";
    for (const auto& feature : features) out << "| " << feature.plan.name << " | " << (feature.capabilityReported ? "yes" : "no") << " | " << feature.availability << " | " << feature.current << " | " << (feature.capabilityReported && feature.detail.PerAppSupport ? "yes" : "no") << " | " << (feature.capabilityReported && (feature.detail.FeatureMiscSupport & CTL_3D_FEATURE_MISC_FLAG_LIVE_CHANGE) ? "yes" : "no") << " | " << feature.verification << " |\n";
    out << "\n## Archived Intel controls\n\n- Sharpening Filter: `KnownIneffective`; prior B580 1→3→5 Set/Get had no visible difference.\n- Anisotropic Filtering and Adaptive Tessellation: `KnownRemoved`; retained only as read-only research entries.\n\n## Next-stage candidates\n\nOnly an actual `Supported` feature with `ReadVerified` is eligible for a later explicit Backup → Set → Get → effect check → Restore test. This report itself does not authorize or perform Set operations.\n";
}
}

int main(int argc, char** argv) {
    if (argc < 2 || std::string(argv[1]) != "probe" || argc > 3) { std::cerr << "Usage: ShadePilot.DriverLab.exe probe [output-directory]\n"; return 2; }
    const fs::path output = argc == 3 ? fs::path(argv[2]) : fs::path("reports"); std::error_code error; fs::create_directories(output / "logs", error); if (error) { std::cerr << "Cannot create output directory: " << error.message() << "\n"; return 3; }
    std::ofstream log(output / "logs" / "driver-enhancement.log", std::ios::trunc); const std::string timestamp = Now(); auto Log = [&](const std::string& text) { const auto line = "[" + Now() + "] " + text; std::cout << line << "\n"; if (log) log << line << "\n"; }; Log("ShadePilot.DriverLab Intel probe started; read-only mode");
    ctl_init_args_t init{}; init.Size = sizeof(init); init.Version = 0; init.AppVersion = CTL_MAKE_VERSION(CTL_IMPL_MAJOR_VERSION, CTL_IMPL_MINOR_VERSION); init.flags = 0; ctl_api_handle_t api = nullptr; const ctl_result_t initResult = ctlInit(&init, &api);
    const fs::path jsonPath = output / "intel_3d_capability_report.json"; std::ofstream json(jsonPath, std::ios::trunc); if (!json) { Log("Cannot create JSON report"); return 3; }
    json << "{\n  \"probe\": \"ShadePilot.DriverLab\",\n  \"mode\": \"read-only\",\n  \"timestamp\": " << Json(timestamp) << ",\n" << "  \"igcl\": {\"initResult\": " << Json(Hex(initResult)) << ", \"initSuccess\": " << (initResult == CTL_RESULT_SUCCESS ? "true" : "false") << ", \"requestedVersion\": " << init.AppVersion << ", \"supportedVersion\": " << init.SupportedVersion << "},\n  \"adapters\": [\n";
    if (initResult != CTL_RESULT_SUCCESS || !api) { json << "  ],\n  \"status\": \"ApiUnavailable\"\n}\n"; Log("IGCL initialization failed: " + Hex(initResult)); return 0; }
    uint32_t count = 0; ctl_result_t enumResult = ctlEnumerateDevices(api, &count, nullptr); Log("ctlEnumerateDevices(count)=" + Hex(enumResult) + ", count=" + std::to_string(count)); std::vector<ctl_device_adapter_handle_t> adapters(count); if (enumResult == CTL_RESULT_SUCCESS && count > 0) enumResult = ctlEnumerateDevices(api, &count, adapters.data());
    for (uint32_t index = 0; index < count && enumResult == CTL_RESULT_SUCCESS; ++index) {
        ctl_device_adapter_properties_t properties{}; properties.Size = sizeof(properties); const ctl_result_t propertiesResult = ctlGetDeviceProperties(adapters[index], &properties); const std::string name = propertiesResult == CTL_RESULT_SUCCESS ? properties.name : "Not exposed by current IGCL version"; Log("adapter[" + std::to_string(index) + "] properties=" + Hex(propertiesResult) + ", name=" + name);
        ctl_3d_feature_caps_t caps{}; caps.Size = sizeof(caps); const ctl_result_t capsCountResult = ctlGetSupported3DCapabilities(adapters[index], &caps); std::vector<ctl_3d_feature_details_t> details(capsCountResult == CTL_RESULT_SUCCESS ? caps.NumSupportedFeatures : 0); ctl_result_t capsDetailsResult = capsCountResult; if (!details.empty()) { caps.pFeatureDetails = details.data(); capsDetailsResult = ctlGetSupported3DCapabilities(adapters[index], &caps); } Log("adapter[" + std::to_string(index) + "] 3D capability count=" + Hex(capsCountResult) + ", details=" + Hex(capsDetailsResult) + ", count=" + std::to_string(details.size()));
        std::map<int, ctl_3d_feature_details_t> featureMap; if (capsDetailsResult == CTL_RESULT_SUCCESS) for (const auto& detail : details) featureMap.emplace(static_cast<int>(detail.FeatureType), detail); std::vector<FeatureResult> results; for (const auto& plan : kFeatures) results.push_back(ProbeFeature(plan, featureMap, adapters[index]));
        if (index) json << ",\n"; json << "    {\n      \"index\": " << index << ",\n      \"propertiesResult\": " << Json(Hex(propertiesResult)) << ",\n      \"name\": " << Json(name) << ",\n      \"vendorId\": " << properties.pci_vendor_id << ",\n      \"deviceId\": " << properties.pci_device_id << ",\n      \"driverVersionRaw\": " << properties.driver_version << ",\n      \"capabilityCountResult\": " << Json(Hex(capsCountResult)) << ",\n      \"capabilityDetailsResult\": " << Json(Hex(capsDetailsResult)) << ",\n      \"features\": [\n"; for (size_t featureIndex = 0; featureIndex < results.size(); ++featureIndex) { if (featureIndex) json << ",\n"; WriteFeatureJson(json, results[featureIndex]); } json << "\n      ]\n    }";
        if (index == 0) WriteMarkdown(output / "intel_3d_capability_report.md", timestamp, init, Hex(initResult), name, properties.pci_device_id, properties.driver_version, results);
    }
    json << "\n  ],\n  \"status\": " << Json(enumResult == CTL_RESULT_SUCCESS ? "Read-only probe completed" : "Adapter enumeration failed") << "\n}\n"; ctlClose(api); Log("Probe completed; no IGCL Set call was made"); return 0;
}
