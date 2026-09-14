// ShadePilot DriverProbe - Phase 1, read-only Intel IGCL capability probe.
// No IGCL Set function is called by this program.
#include <windows.h>
#include <ctime>
#include <fstream>
#include <iomanip>
#include <iostream>
#include <sstream>
#include <string>
#include <vector>
#include "igcl_api.h"

namespace {
std::string Json(const std::string& s) {
  std::ostringstream o; o << '"';
  for (unsigned char c : s) { if (c=='\\') o<<"\\\\"; else if(c=='"') o<<"\\\""; else if(c=='\n') o<<"\\n"; else if(c=='\r') o<<"\\r"; else o<<(c<0x20?'?':static_cast<char>(c)); }
  o << '"'; return o.str();
}
std::string Hex(ctl_result_t r) { std::ostringstream o; o<<"0x"<<std::uppercase<<std::hex<<static_cast<uint32_t>(r); return o.str(); }
std::string Now() { std::time_t t=std::time(nullptr); std::tm tm{}; localtime_s(&tm,&t); std::ostringstream o; o<<std::put_time(&tm,"%Y-%m-%d %H:%M:%S"); return o.str(); }
struct Logger { std::ofstream f; explicit Logger(const char* p):f(p,std::ios::trunc){} void W(const std::string& s){std::string x="["+Now()+"] "+s; std::cout<<x<<std::endl; if(f)f<<x<<std::endl;} };
std::string FeatureName(ctl_3d_feature_t f) {
  switch(f) {
    case CTL_3D_FEATURE_FRAME_PACING:return "Frame Pacing";
    case CTL_3D_FEATURE_ENDURANCE_GAMING:return "Endurance Gaming";
    case CTL_3D_FEATURE_GAMING_FLIP_MODES:return "Gaming Flip Modes";
    case CTL_3D_FEATURE_ADAPTIVE_SYNC_PLUS:return "Adaptive Sync Plus";
    case CTL_3D_FEATURE_APP_PROFILES:return "App Profiles";
    case CTL_3D_FEATURE_APP_PROFILE_DETAILS:return "App Profile Details";
    case CTL_3D_FEATURE_EMULATED_TYPED_64BIT_ATOMICS:return "Emulated Typed 64-bit Atomics";
    case CTL_3D_FEATURE_VRR_WINDOWED_BLT:return "VRR Windowed BLT";
    case CTL_3D_FEATURE_GLOBAL_OR_PER_APP:return "Global Or Per-App";
    case CTL_3D_FEATURE_FRAME_GENERATION:return "Frame Generation";
    case CTL_3D_FEATURE_PREBUILT_SHADER_DOWNLOAD:return "Prebuilt Shader Download";
    case CTL_3D_FEATURE_LIVE_STATE:return "Live State";
    case CTL_3D_FEATURE_FRAME_GENERATION_CONTROL:return "Frame Generation Control";
    case CTL_3D_FEATURE_SHARPENING_FILTER:return "Sharpening Filter";
    case CTL_3D_FEATURE_ANISOTROPIC:return "Anisotropic Filtering";
    case CTL_3D_FEATURE_TEXTURE_FILTERING_QUALITY:return "Texture Filtering Quality";
    case CTL_3D_FEATURE_CMAA:return "CMAA";
    case CTL_3D_FEATURE_MSAA:return "MSAA";
    case CTL_3D_FEATURE_LOW_LATENCY:return "Low Latency";
    case CTL_3D_FEATURE_FRAME_LIMIT:return "Frame Limit";
    default:return "Unknown Feature";
  }
}
}

int main() {
  CreateDirectoryA("logs",nullptr); Logger log("logs/driver_probe.log");
  log.W("ShadePilot.DriverProbe Phase 1 started; read-only mode");
  ctl_init_args_t init{}; init.Size=sizeof(init); init.Version=0; init.AppVersion=CTL_MAKE_VERSION(CTL_IMPL_MAJOR_VERSION,CTL_IMPL_MINOR_VERSION); init.flags=0;
  ctl_api_handle_t api=nullptr; ctl_result_t r=ctlInit(&init,&api);
  std::ofstream report("driver_probe_report.json",std::ios::trunc);
  if(!report){log.W("ERROR: cannot create driver_probe_report.json");return 2;}
  report<<"{\n  \"probe\": \"ShadePilot.DriverProbe\",\n  \"mode\": \"read-only\",\n  \"timestamp\": "<<Json(Now())<<",\n  \"igcl\": {\n    \"initResult\": "<<Json(Hex(r))<<",\n    \"initSuccess\": "<<(r==CTL_RESULT_SUCCESS?"true":"false")<<",\n    \"requestedVersion\": "<<init.AppVersion<<",\n    \"supportedVersion\": "<<init.SupportedVersion<<"\n  },\n";
  if(r!=CTL_RESULT_SUCCESS||!api){log.W("IGCL initialization failed: "+Hex(r)); report<<"  \"adapters\": [],\n  \"status\": \"Driver Enhancement Unsupported\"\n}\n"; return 0;}
  log.W("IGCL initialized; implementation version="+std::to_string(init.SupportedVersion));
  uint32_t n=0; r=ctlEnumerateDevices(api,&n,nullptr); log.W("ctlEnumerateDevices(count)="+Hex(r)+", count="+std::to_string(n));
  std::vector<ctl_device_adapter_handle_t> adapters(n); if(r==CTL_RESULT_SUCCESS&&n)r=ctlEnumerateDevices(api,&n,adapters.data());
  report<<"  \"adapters\": [\n";
  for(uint32_t ai=0;ai<n;++ai){
    ctl_device_adapter_properties_t p{}; p.Size=sizeof(p); ctl_result_t pr=ctlGetDeviceProperties(adapters[ai],&p);
    log.W("adapter["+std::to_string(ai)+"] properties="+Hex(pr)); if(ai)report<<",\n";
    report<<"    {\n      \"propertiesResult\": "<<Json(Hex(pr))<<",\n      \"name\": "<<Json(pr==CTL_RESULT_SUCCESS?p.name:"Not exposed by current IGCL version")<<",\n      \"vendorId\": "<<p.pci_vendor_id<<",\n      \"deviceId\": "<<p.pci_device_id<<",\n      \"revisionId\": "<<p.rev_id<<",\n      \"driverVersion\": "<<p.driver_version<<",\n      \"supportedSubfunctions\": "<<p.supported_subfunction_flags<<",\n      \"displayOutputs\": [\n";
    uint32_t on=0; ctl_result_t orr=ctlEnumerateDisplayOutputs(adapters[ai],&on,nullptr); log.W("adapter["+std::to_string(ai)+"] outputs(count)="+Hex(orr)+", count="+std::to_string(on)); std::vector<ctl_display_output_handle_t> outputs(on); if(orr==CTL_RESULT_SUCCESS&&on)orr=ctlEnumerateDisplayOutputs(adapters[ai],&on,outputs.data());
    for(uint32_t oi=0;oi<on;++oi){
      ctl_sharpness_caps_t caps{}; caps.Size=sizeof(caps); ctl_result_t cr=ctlGetSharpnessCaps(outputs[oi],&caps); std::vector<ctl_sharpness_filter_properties_t> filters;
      if(cr==CTL_RESULT_SUCCESS&&caps.NumFilterTypes){filters.resize(caps.NumFilterTypes);caps.pFilterProperty=filters.data();cr=ctlGetSharpnessCaps(outputs[oi],&caps);}
      ctl_sharpness_settings_t cur{}; cur.Size=sizeof(cur); ctl_result_t gr=ctlGetCurrentSharpness(outputs[oi],&cur);
      ctl_display_properties_t dp{}; dp.Size=sizeof(dp); ctl_result_t dpr=ctlGetDisplayProperties(outputs[oi],&dp);
      ctl_adapter_display_encoder_properties_t ep{}; ep.Size=sizeof(ep); ctl_result_t epr=ctlGetAdaperDisplayEncoderProperties(outputs[oi],&ep);
      ctl_scaling_caps_t scaps{}; scaps.Size=sizeof(scaps); ctl_result_t scrc=ctlGetSupportedScalingCapability(outputs[oi],&scaps);
      ctl_scaling_settings_t sc{}; sc.Size=sizeof(sc); ctl_result_t scr=ctlGetCurrentScaling(outputs[oi],&sc);
      log.W("adapter["+std::to_string(ai)+"] output["+std::to_string(oi)+"] caps="+Hex(cr)+", current="+Hex(gr)+", displayProperties="+Hex(dpr)+", encoderProperties="+Hex(epr)+", scalingCaps="+Hex(scrc)+", scalingCurrent="+Hex(scr)+", scalingEnabled="+(scr==CTL_RESULT_SUCCESS?(sc.Enable?"true":"false"):"n/a")+", scalingType="+(scr==CTL_RESULT_SUCCESS?std::to_string(sc.ScalingType):"n/a")+", preferredScalingType="+(scr==CTL_RESULT_SUCCESS?std::to_string(sc.PreferredScalingType):"n/a")+", customX="+(scr==CTL_RESULT_SUCCESS?std::to_string(sc.CustomScalingX):"n/a")+", customY="+(scr==CTL_RESULT_SUCCESS?std::to_string(sc.CustomScalingY):"n/a")); if(oi)report<<",\n";
      report<<"        {\n          \"displayPropertiesResult\": "<<Json(Hex(dpr))<<",\n          \"displayType\": "<<(dpr==CTL_RESULT_SUCCESS?dp.Type:0)<<",\n          \"attachedDisplayMuxType\": "<<(dpr==CTL_RESULT_SUCCESS?dp.AttachedDisplayMuxType:0)<<",\n          \"protocolConverterOutput\": "<<(dpr==CTL_RESULT_SUCCESS?dp.ProtocolConverterOutput:0)<<",\n          \"displayConfigFlags\": "<<(dpr==CTL_RESULT_SUCCESS?dp.DisplayConfigFlags:0)<<",\n          \"featureEnabledFlags\": "<<(dpr==CTL_RESULT_SUCCESS?dp.FeatureEnabledFlags:0)<<",\n          \"featureSupportedFlags\": "<<(dpr==CTL_RESULT_SUCCESS?dp.FeatureSupportedFlags:0)<<",\n          \"encoderPropertiesResult\": "<<Json(Hex(epr))<<",\n          \"encoderConfigFlags\": "<<(epr==CTL_RESULT_SUCCESS?ep.EncoderConfigFlags:0)<<",\n          \"encoderType\": "<<(epr==CTL_RESULT_SUCCESS?ep.Type:0)<<",\n          \"capsResult\": "<<Json(Hex(cr))<<",\n          \"supportedFilterFlags\": "<<caps.SupportedFilterFlags<<",\n          \"filters\": [";
      for(size_t fi=0;fi<filters.size();++fi){if(fi)report<<",";auto& f=filters[fi];report<<"{\"type\": "<<f.FilterType<<", \"min\": "<<f.FilterDetails.min_possible_value<<", \"max\": "<<f.FilterDetails.max_possible_value<<", \"step\": "<<f.FilterDetails.step_size<<"}";}
      report<<"],\n          \"currentResult\": "<<Json(Hex(gr))<<",\n          \"current\": {\"enabled\": "<<(gr==CTL_RESULT_SUCCESS&&cur.Enable?"true":"false")<<", \"filterType\": "<<(gr==CTL_RESULT_SUCCESS?cur.FilterType:0)<<", \"intensity\": "<<(gr==CTL_RESULT_SUCCESS?cur.Intensity:0)<<"}\n        }";
    }
    report<<"\n      ],\n      \"features3D\": {\n        \"queryResult\": ";
    ctl_3d_feature_caps_t fc{}; fc.Size=sizeof(fc); ctl_result_t fr=ctlGetSupported3DCapabilities(adapters[ai],&fc); log.W("adapter["+std::to_string(ai)+"] 3D caps(count)="+Hex(fr)+", count="+std::to_string(fc.NumSupportedFeatures));
    report<<Json(Hex(fr))<<",\n";
    ctl_result_t detailsResult=fr;
    std::vector<ctl_3d_feature_details_t> details;
    if(fr==CTL_RESULT_SUCCESS&&fc.NumSupportedFeatures){details.resize(fc.NumSupportedFeatures);fc.pFeatureDetails=details.data();detailsResult=ctlGetSupported3DCapabilities(adapters[ai],&fc);}
    report<<"        \"detailsResult\": "<<Json(Hex(detailsResult))<<",\n        \"features\": [\n";
    for(uint32_t fi=0;fi<details.size();++fi){if(fi)report<<",\n";auto& d=details[fi];report<<"          {\"feature\": "<<d.FeatureType<<", \"name\": "<<Json(FeatureName(d.FeatureType))<<", \"valueType\": "<<d.ValueType<<", \"perAppSupport\": "<<(d.PerAppSupport?"true":"false")<<", \"liveChange\": "<<((d.FeatureMiscSupport & CTL_3D_FEATURE_MISC_FLAG_LIVE_CHANGE)?"true":"false")<<", \"conflictingFeatures\": "<<d.ConflictingFeatures<<", \"featureMiscSupport\": "<<d.FeatureMiscSupport<<"}";}
    report<<"        ]\n      }\n    }";
  }
  report<<"\n  ],\n  \"status\": \"Read-only probe completed\"\n}\n"; report.close(); ctlClose(api); log.W("Probe completed; no IGCL Set function was called"); return 0;
}
