// GPL-3.0-or-later. Hardware-only D3D11 compute; never substitutes WARP for GPU mode.
#include "gpu.h"
#include <windows.h>
#include <d3d11.h>
#include <dxgi1_2.h>
#include <d3dcompiler.h>
#include <wrl/client.h>
#include <vector>
#include <stdexcept>
#include <sstream>
using Microsoft::WRL::ComPtr;
static void check(HRESULT hr,const char* action) {
    if(FAILED(hr)){std::ostringstream s;s<<action<<" (0x"<<std::hex<<uint32_t(hr)<<")";throw std::runtime_error(s.str());}
}
static std::string utf8(const wchar_t* s) {
    int n=WideCharToMultiByte(CP_UTF8,0,s,-1,nullptr,0,nullptr,nullptr);
    std::vector<char> b(n);WideCharToMultiByte(CP_UTF8,0,s,-1,b.data(),n,nullptr,nullptr);return b.data();
}
static std::string hardware(ComPtr<ID3D11Device>& device,ComPtr<ID3D11DeviceContext>& context) {
    ComPtr<IDXGIFactory1> factory;check(CreateDXGIFactory1(IID_PPV_ARGS(&factory)),"DXGI initialization failed");
    for(UINT i=0;;i++) {
        ComPtr<IDXGIAdapter1> adapter;
        HRESULT enumerated=factory->EnumAdapters1(i,&adapter);if(enumerated==DXGI_ERROR_NOT_FOUND)break;
        check(enumerated,"GPU enumeration failed");DXGI_ADAPTER_DESC1 desc={};adapter->GetDesc1(&desc);
        if(desc.Flags&DXGI_ADAPTER_FLAG_SOFTWARE)continue;
        D3D_FEATURE_LEVEL requested=D3D_FEATURE_LEVEL_11_0,actual;
        if(SUCCEEDED(D3D11CreateDevice(adapter.Get(),D3D_DRIVER_TYPE_UNKNOWN,nullptr,0,&requested,1,
                D3D11_SDK_VERSION,&device,&actual,&context)))return utf8(desc.Description);
    }
    throw std::runtime_error("No hardware GPU with Direct3D feature level 11.0 is available. Update the graphics driver or select CPU.");
}
std::string gpu_device_name() {
    ComPtr<ID3D11Device> d;ComPtr<ID3D11DeviceContext> c;return hardware(d,c);
}
struct Params {uint32_t width,height,outWidth,emitWidth,rowScale,phase,edgeStrength,scanline,rx,ry,pad0,pad1;};
class D3dProcessor final : public GpuProcessor {
    ComPtr<ID3D11Device> device;
    ComPtr<ID3D11DeviceContext> context;
    ComPtr<ID3D11ComputeShader> shader;
    ComPtr<ID3D11Buffer> input,kernel,output,staging,constants;
    ComPtr<ID3D11ShaderResourceView> inputView,kernelView;
    ComPtr<ID3D11UnorderedAccessView> outputView;
    Params params;
    std::vector<uint32_t> upload;
    ComPtr<ID3D11Buffer> buffer(UINT bytes,UINT bind,D3D11_USAGE usage,UINT cpu,UINT misc,const void* data=nullptr) {
        D3D11_BUFFER_DESC desc={};desc.ByteWidth=bytes;desc.BindFlags=bind;desc.Usage=usage;
        desc.CPUAccessFlags=cpu;desc.MiscFlags=misc;desc.StructureByteStride=misc?4:0;
        D3D11_SUBRESOURCE_DATA initial={};initial.pSysMem=data;ComPtr<ID3D11Buffer> b;
        check(device->CreateBuffer(&desc,data?&initial:nullptr,&b),"GPU buffer allocation failed");return b;
    }
    ComPtr<ID3D11ShaderResourceView> view(ID3D11Buffer* b,UINT count) {
        D3D11_SHADER_RESOURCE_VIEW_DESC d={};d.Format=DXGI_FORMAT_UNKNOWN;d.ViewDimension=D3D11_SRV_DIMENSION_BUFFER;
        d.Buffer.NumElements=count;ComPtr<ID3D11ShaderResourceView> v;
        check(device->CreateShaderResourceView(b,&d,&v),"GPU input view failed");return v;
    }
public:
    D3dProcessor(const uint32_t* table,int tableCount,Params p,const wchar_t* path):params(p) {
        hardware(device,context);
        ComPtr<ID3DBlob> code,errors;
        HRESULT hr=D3DCompileFromFile(path,nullptr,nullptr,"main","cs_5_0",
            D3DCOMPILE_OPTIMIZATION_LEVEL3|D3DCOMPILE_IEEE_STRICTNESS,0,&code,&errors);
        if(FAILED(hr))throw std::runtime_error(errors?std::string(static_cast<const char*>(errors->GetBufferPointer()),errors->GetBufferSize()):"Cannot load assets/ntsc.hlsl");
        check(device->CreateComputeShader(code->GetBufferPointer(),code->GetBufferSize(),nullptr,&shader),"GPU shader creation failed");
        UINT inputCount=p.width*p.height,outputCount=p.emitWidth*p.height*p.rowScale;
        upload.resize(inputCount);
        input=buffer(inputCount*4,D3D11_BIND_SHADER_RESOURCE,D3D11_USAGE_DEFAULT,0,D3D11_RESOURCE_MISC_BUFFER_STRUCTURED);
        kernel=buffer(tableCount*4,D3D11_BIND_SHADER_RESOURCE,D3D11_USAGE_IMMUTABLE,0,D3D11_RESOURCE_MISC_BUFFER_STRUCTURED,table);
        output=buffer(outputCount*4,D3D11_BIND_UNORDERED_ACCESS,D3D11_USAGE_DEFAULT,0,D3D11_RESOURCE_MISC_BUFFER_STRUCTURED);
        staging=buffer(outputCount*4,0,D3D11_USAGE_STAGING,D3D11_CPU_ACCESS_READ,0);
        constants=buffer(sizeof(Params),D3D11_BIND_CONSTANT_BUFFER,D3D11_USAGE_DEFAULT,0,0);
        inputView=view(input.Get(),inputCount);kernelView=view(kernel.Get(),tableCount);
        D3D11_UNORDERED_ACCESS_VIEW_DESC u={};u.Format=DXGI_FORMAT_UNKNOWN;u.ViewDimension=D3D11_UAV_DIMENSION_BUFFER;
        u.Buffer.NumElements=outputCount;check(device->CreateUnorderedAccessView(output.Get(),&u,&outputView),"GPU output view failed");
    }
    void process(const unsigned char* rgb,unsigned char* dest,int phase) override {
        for(size_t n=0;n<upload.size();n++)upload[n]=uint32_t(rgb[n*3])|(uint32_t(rgb[n*3+1])<<8)|(uint32_t(rgb[n*3+2])<<16);
        params.phase=phase;
        context->UpdateSubresource(input.Get(),0,nullptr,upload.data(),0,0);
        context->UpdateSubresource(constants.Get(),0,nullptr,&params,0,0);
        ID3D11ShaderResourceView* views[]={inputView.Get(),kernelView.Get()};
        ID3D11UnorderedAccessView* uav=outputView.Get();ID3D11Buffer* cb=constants.Get();
        context->CSSetShader(shader.Get(),nullptr,0);context->CSSetShaderResources(0,2,views);
        context->CSSetUnorderedAccessViews(0,1,&uav,nullptr);context->CSSetConstantBuffers(0,1,&cb);
        context->Dispatch((params.emitWidth+63)/64,params.height,1);
        ID3D11UnorderedAccessView* empty=nullptr;context->CSSetUnorderedAccessViews(0,1,&empty,nullptr);
        context->CopyResource(staging.Get(),output.Get());D3D11_MAPPED_SUBRESOURCE mapped={};
        check(context->Map(staging.Get(),0,D3D11_MAP_READ,0,&mapped),"GPU readback failed");
        const uint32_t* pixels=static_cast<const uint32_t*>(mapped.pData);
        size_t count=size_t(params.emitWidth)*params.height*params.rowScale;
        for(size_t n=0;n<count;n++){uint32_t p=pixels[n];dest[n*3]=p&255;dest[n*3+1]=(p>>8)&255;dest[n*3+2]=(p>>16)&255;}
        context->Unmap(staging.Get(),0);
    }
};
std::unique_ptr<GpuProcessor> create_gpu(const uint32_t* table,int tableCount,int width,int height,int outWidth,
    int emitWidth,int rowScale,int edgeStrength,int scanline,int rx,int ry,const wchar_t* shader) {
    Params p={uint32_t(width),uint32_t(height),uint32_t(outWidth),uint32_t(emitWidth),uint32_t(rowScale),0,
        uint32_t(edgeStrength),uint32_t(scanline),uint32_t(rx),uint32_t(ry),0,0};
    return std::unique_ptr<GpuProcessor>(new D3dProcessor(table,tableCount,p,shader));
}
