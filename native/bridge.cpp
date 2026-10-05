// NTSC Video Studio bridge, 2026. Distributed under GPL-3.0-or-later.
// Uses the LGPL-2.1-or-later snes_ntsc library from the supplied MesenCE archive.
#include "snes_ntsc.h"
#include <vector>
#include <new>
#include <cmath>
#include <algorithm>
#include <cstdint>
#include <memory>

struct Filter {
    snes_ntsc_t ntsc;
    int width, height, outWidth, emitWidth, rowScale;
    std::vector<unsigned short> input;
    std::vector<uint32_t> output, alternate;
    int scanline, edgeStrength, patternSize, displayWidth, displayHeight;
    std::vector<unsigned char> edges;
};

extern "C" __declspec(dllexport) void* __cdecl ntsc_create(
    int width, int height, const double* options, int edgeStrength, int scanline,
    int patternSize, int displayWidth, int displayHeight) {
    if (!options || width < 4 || height < 1 || width > 8192 || height > 8192 ||
        size_t(width)*height>33554432 || (width-1)%3 || displayWidth<2 || displayHeight<2) return nullptr;
    for(int i=0;i<10;i++) if(!std::isfinite(options[i]) || options[i]<-1 || options[i]>1) return nullptr;
    try {
        std::unique_ptr<Filter> f(new Filter());
        f->width=width; f->height=height; f->outWidth=SNES_NTSC_OUT_WIDTH(width);
        // Bound pipe frames: retain the full filter internally, resample before transfer.
        bool large=size_t(f->outWidth)*height*6>100000000;
        f->rowScale=large?1:2;f->emitWidth=large?displayWidth:f->outWidth;
        f->input.resize(width); f->output.resize(f->outWidth); f->alternate.resize(f->outWidth);
        f->edges.resize(width);
        f->scanline=std::max(0,std::min(100,scanline));
        f->edgeStrength=std::max(20,std::min(100,edgeStrength));
        f->patternSize=std::max(1,std::min(8,patternSize));
        f->displayWidth=displayWidth; f->displayHeight=displayHeight;
        snes_ntsc_setup_t s={};
        s.hue=options[0]; s.saturation=options[1]; s.contrast=options[2]; s.brightness=options[3];
        s.sharpness=options[4]; s.gamma=options[5]; s.resolution=options[6];
        // Keep cross-talk active in every style; do not let library presets merge phases.
        s.artifacts=std::max(-0.75,options[7]); s.fringing=std::max(-0.75,options[8]);
        s.bleed=options[9]; s.merge_fields=0;
        snes_ntsc_init(&f->ntsc,&s);
        return f.release();
    } catch (...) { return nullptr; }
}

extern "C" __declspec(dllexport) int __cdecl ntsc_process(
    void* handle, const unsigned char* rgb, int inputBytes,
    unsigned char* dest, int outputBytes, int frame) {
    Filter* f=static_cast<Filter*>(handle);
    if(!f || !rgb || !dest || inputBytes!=f->width*f->height*3 || outputBytes!=f->emitWidth*f->height*f->rowScale*3) return 0;
    const int phase=((frame%2)+2)%2;
    // Studio supplies working dimensions: rainbow and edge footprints share the filter grid.
    int rx=std::max(1,(f->width*f->patternSize+f->displayWidth-1)/f->displayWidth);
    int ry=std::max(1,(f->height*f->patternSize+f->displayHeight-1)/f->displayHeight);
    for(int y=0;y<f->height;++y) {
      for(int x=0;x<f->width;++x) {
        size_t left=(size_t(y)*f->width+std::max(0,x-rx))*3;
        size_t right=(size_t(y)*f->width+std::min(f->width-1,x+rx))*3;
        size_t top=(size_t(std::max(0,y-ry))*f->width+x)*3;
        size_t bottom=(size_t(std::min(f->height-1,y+ry))*f->width+x)*3;
        int gradient=0;
        for(int c=0;c<3;++c)gradient=std::max(gradient,std::max(std::abs(int(rgb[left+c])-rgb[right+c]),std::abs(int(rgb[top+c])-rgb[bottom+c])));
        f->edges[x]=static_cast<unsigned char>(std::max(0,std::min(255,(gradient-8)*4)));
        size_t source=(size_t(y)*f->width+x)*3;
        f->input[x]=static_cast<unsigned short>((rgb[source]>>3) | ((rgb[source+1]>>3)<<5) | ((rgb[source+2]>>3)<<10));
      }
      // Original blit advances burst phase once per row. Preserve it with a row buffer.
      snes_ntsc_blit(&f->ntsc,f->input.data(),f->width,(phase+y)%3,
          f->width,1,f->output.data(),f->outWidth*4);
      snes_ntsc_blit(&f->ntsc,f->input.data(),f->width,(1-phase+y)%3,
          f->width,1,f->alternate.data(),f->outWidth*4);
      for(int x=0;x<f->emitWidth;++x) {
        double sx=(x+0.5)*f->outWidth/f->emitWidth-0.5;
        int x0=std::max(0,std::min(f->outWidth-1,int(std::floor(sx))));
        int x1=std::min(f->outWidth-1,x0+1);float mix=float(std::max(0.0,sx-x0));
        uint32_t p0=f->output[x0],p1=f->output[x1];
        auto component=[&](int shift){return float((p0>>shift)&255)*(1-mix)+float((p1>>shift)&255)*mix;};
        uint32_t a0=f->alternate[x0],a1=f->alternate[x1];
        auto other=[&](int shift){return float((a0>>shift)&255)*(1-mix)+float((a1>>shift)&255)*mix;};
        int sourceX=std::min(f->width-1,int(int64_t(x)*f->width/f->emitWidth));
        float amount=f->edges[sourceX]/255.0f*f->edgeStrength/100.0f;
        // Enhance only genuine Blargg phase differences; invent no rainbow stripes.
        auto enhanced=[&](int shift){float c=component(shift),a=other(shift);return std::max(0,std::min(255,int(c+(c-a)*1.5f*amount)));};
        int r=enhanced(16),g=enhanced(8),b=enhanced(0);
        for(int row=0;row<f->rowScale;++row) {
            unsigned char* d=dest+(size_t(y*f->rowScale+row)*f->emitWidth+x)*3;
            int strength=(f->rowScale==2?row==1:(y&1)!=0)?100-f->scanline:100;
            d[0]=r*strength/100;
            d[1]=g*strength/100;
            d[2]=b*strength/100;
        }
      }
    }
    return 1;
}

extern "C" __declspec(dllexport) void __cdecl ntsc_destroy(void* handle) { delete static_cast<Filter*>(handle); }
