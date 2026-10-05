// GPL-3.0-or-later. Blargg 3->7 lookup evaluation and edge enhancement on D3D11.
// LUT layout and packed arithmetic derive from snes_ntsc.h (Shay Green, LGPL-2.1+).
StructuredBuffer<uint> Pixels : register(t0);
StructuredBuffer<uint> Kernels : register(t1);
RWStructuredBuffer<uint> Output : register(u0);
cbuffer Parameters : register(b0) {
    uint Width,Height,OutWidth,EmitWidth;
    uint RowScale,Phase,EdgeStrength,Scanline;
    uint RadiusX,RadiusY,Pad0,Pad1;
};
uint color(int x,uint y) {
    if(x<0||x>=(int)Width)return 0;
    uint p=Pixels[y*Width+x];
    return ((p&255)>>3)|((((p>>8)&255)>>3)<<5)|((((p>>16)&255)>>3)<<10);
}
uint lookup(int x,uint y,uint burst,uint offset) {
    uint n=color(x,y);
    uint base=((n<<9&0x3C00)|(n&0x03E0)|(n>>10&0x001E))*64;
    return Kernels[base+burst*42+offset];
}
uint blargg(uint x,uint y,uint phase) {
    int g=(int)(x/7);uint r=x%7,burst=(phase+y)%3;
    int c0=3*g+1,p0=3*g-2;
    int c1=r<2?3*g-1:3*g+2,p1=r<2?3*g-4:3*g-1;
    int c2=r<4?3*g:3*g+3,p2=r<4?3*g-3:3*g;
    uint raw=lookup(c0,y,burst,r)+lookup(c1,y,burst,(r+12)%7+14)+lookup(c2,y,burst,(r+10)%7+28)
        +lookup(p0,y,burst,(r+7)%14)+lookup(p1,y,burst,(r+5)%7+21)+lookup(p2,y,burst,(r+3)%7+35);
    uint builder=(1u<<21)|(1u<<11)|(1u<<1);
    uint sub=(raw>>8)&(builder*3/2),clamp=builder*0x101-sub;
    raw|=clamp;clamp-=sub;raw&=clamp;
    return 0xFF000000|((raw>>4)&0xFF0000)|((raw>>2)&0xFF00)|(raw&0xFF);
}
float3 rgb(uint p){return float3((p>>16)&255,(p>>8)&255,p&255);}
int3 sourceRgb(uint x,uint y){uint p=Pixels[y*Width+x];return int3(p&255,(p>>8)&255,(p>>16)&255);}
[numthreads(64,1,1)]
void main(uint3 id:SV_DispatchThreadID) {
    uint x=id.x,y=id.y;if(x>=EmitWidth||y>=Height)return;
    float sx=(x+0.5)*OutWidth/EmitWidth-0.5;
    uint x0=(uint)clamp(floor(sx),0.0,(float)(OutWidth-1)),x1=min(OutWidth-1,x0+1);
    float mix=max(0.0,sx-x0);
    float3 c=lerp(rgb(blargg(x0,y,Phase)),rgb(blargg(x1,y,Phase)),mix);
    float3 a=lerp(rgb(blargg(x0,y,1-Phase)),rgb(blargg(x1,y,1-Phase)),mix);
    uint px=min(Width-1,x*Width/EmitWidth);
    uint left=px>RadiusX?px-RadiusX:0,right=min(Width-1,px+RadiusX);
    uint top=y>RadiusY?y-RadiusY:0,bottom=min(Height-1,y+RadiusY);
    int3 delta=max(abs(sourceRgb(left,y)-sourceRgb(right,y)),abs(sourceRgb(px,top)-sourceRgb(px,bottom)));
    int gradient=max(delta.r,max(delta.g,delta.b));
    float amount=clamp((gradient-8)*4,0,255)/255.0*EdgeStrength/100.0;
    uint3 enhanced=(uint3)clamp(c+(c-a)*1.5*amount,0.0,255.0);
    for(uint row=0;row<RowScale;row++) {
        uint strength=(RowScale==2?row==1:(y&1)!=0)?100-Scanline:100;
        uint3 v=enhanced*strength/100;
        Output[(y*RowScale+row)*EmitWidth+x]=v.r|(v.g<<8)|(v.b<<16);
    }
}
