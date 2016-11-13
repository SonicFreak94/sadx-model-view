﻿using System;
using System.IO;
using sadx_model_view.Ninja;
using SharpDX;
using SharpDX.Direct3D9;

namespace sadx_model_view.SA1
{
	[Flags]
	enum ColFlags : uint
	{
		Solid      = 0x1,
		Water      = 0x2,
		NoFriction = 0x4,
		Unknown1   = 0x8,
		Unknown2   = 0x10,
		Unknown3   = 0x20,
		Unknown4   = 0x40,
		IncAccel   = 0x80,
		Dig        = 0x100,
		Unknown5   = 0x200,
		Unknown6   = 0x400,
		Unknown7   = 0x800,
		NoClimb    = 0x1000,
		Unknown8   = 0x2000,
		Unknown9   = 0x4000,
		Unknown10  = 0x8000,
		Hurt       = 0x10000,
		Unknown11  = 0x20000,
		Unknown12  = 0x40000,
		Unknown13  = 0x80000,
		Footprints = 0x100000,
		Unknown14  = 0x200000,
		Unknown15  = 0x400000,
		Unknown16  = 0x800000,
		Unknown17  = 0x1000000,
		Unknown18  = 0x2000000,
		Unknown19  = 0x4000000,
		Unknown20  = 0x8000000,
		Unknown21  = 0x10000000,
		Unknown22  = 0x20000000,
		Unknown23  = 0x40000000,
		Visible    = 0x80000000,
	}

	class Col : IDisposable
	{
		public static int SizeInBytes => 0x24;

		public Vector3 Center;
		public float Radius;

		private int pad_a;
		private int pad_b;

		public NJS_OBJECT Object;
		public int anonymous_6;
		public ColFlags Flags;

		public Col(Stream stream)
		{
			var buffer = new byte[SizeInBytes];
			stream.Read(buffer, 0, buffer.Length);
			var position = stream.Position;

			Center = Util.VectorFromStream(ref buffer, 0x00);
			Radius = BitConverter.ToSingle(buffer, 0x04);
			pad_a  = BitConverter.ToInt32(buffer, 0x10);
			pad_b  = BitConverter.ToInt32(buffer, 0x14);

			var object_ptr = BitConverter.ToUInt32(buffer, 0x18);

			if (object_ptr > 0)
			{
				stream.Position = object_ptr;
				Object = new NJS_OBJECT(stream);
			}

			anonymous_6 = BitConverter.ToInt32(buffer, 0x1C);
			Flags = (ColFlags)BitConverter.ToUInt32(buffer, 0x20);

			stream.Position = position;
		}

		public void CommitVertexBuffer(Device device)
		{
			Object?.CommitVertexBuffer(device);
		}

		public void Draw(Device device)
		{
			if (!Flags.HasFlag(ColFlags.Visible))
				return;

			Object?.Draw(device);
		}

		public void Dispose()
		{
			Object?.Dispose();
			Object = null;
		}
	}
}