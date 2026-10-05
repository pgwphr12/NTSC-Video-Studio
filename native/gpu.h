// GPL-3.0-or-later. D3D11 compute backend for the supplied Blargg lookup table.
#pragma once
#include <memory>
#include <string>
#include <cstdint>
class GpuProcessor {
public:
    virtual ~GpuProcessor()=default;
    virtual void process(const unsigned char* rgb,unsigned char* dest,int phase)=0;
};
std::string gpu_device_name();
std::unique_ptr<GpuProcessor> create_gpu(const uint32_t* table,int tableCount,int width,int height,
    int outWidth,int emitWidth,int rowScale,int edgeStrength,int scanline,int rx,int ry,const wchar_t* shader);
