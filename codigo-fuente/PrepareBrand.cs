using System;
using System.IO;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Collections.Generic;

// Conversión del logo aprobado a los tamaños que utiliza Windows.
static class PrepareBrand {
    static int Main(string[] args){
        if(args.Length!=2)return 1;
        var sizes=new[]{16,24,32,48,64,128,256};
        var images=new List<byte[]>();
        using(var source=Image.FromFile(args[0])){
            foreach(int size in sizes){
                using(var bitmap=new Bitmap(size,size,PixelFormat.Format32bppArgb))
                using(var graphics=Graphics.FromImage(bitmap))
                using(var memory=new MemoryStream()){
                    graphics.Clear(Color.Transparent);graphics.CompositingMode=CompositingMode.SourceCopy;
                    graphics.CompositingQuality=CompositingQuality.HighQuality;
                    graphics.InterpolationMode=InterpolationMode.HighQualityBicubic;
                    graphics.PixelOffsetMode=PixelOffsetMode.HighQuality;
                    graphics.DrawImage(source,new Rectangle(0,0,size,size));
                    bitmap.Save(memory,ImageFormat.Png);images.Add(memory.ToArray());
                }
            }
        }
        using(var file=File.Create(args[1]))using(var writer=new BinaryWriter(file)){
            writer.Write((ushort)0);writer.Write((ushort)1);writer.Write((ushort)sizes.Length);
            int offset=6+16*sizes.Length;
            for(int i=0;i<sizes.Length;i++){
                writer.Write((byte)(sizes[i]==256?0:sizes[i]));writer.Write((byte)(sizes[i]==256?0:sizes[i]));
                writer.Write((byte)0);writer.Write((byte)0);writer.Write((ushort)1);writer.Write((ushort)32);
                writer.Write(images[i].Length);writer.Write(offset);offset+=images[i].Length;
            }
            foreach(var bytes in images)writer.Write(bytes);
        }
        return 0;
    }
}
