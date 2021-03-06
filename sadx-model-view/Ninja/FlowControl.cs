﻿namespace sadx_model_view.Ninja
{
	public struct FlowControl
	{
		public bool UseMaterialFlags;

		public NJD_FLAG AndFlags { get; private set; } // Equivalent to: _nj_constant_attr_and_
		public NJD_FLAG OrFlags  { get; private set; } // Equivalent to: _nj_constant_attr_or_

		public void Reset()
		{
			AndFlags         = (NJD_FLAG)0xFFFFFFFF;
			OrFlags          = 0;
			UseMaterialFlags = false;
		}

		public void Add(NJD_FLAG and, NJD_FLAG or)
		{
			AndFlags |= and;
			OrFlags  |= or;
		}

		public void Remove(NJD_FLAG and, NJD_FLAG or)
		{
			AndFlags &= ~and;
			OrFlags  &= ~or;
		}

		public void Set(NJD_FLAG and, NJD_FLAG or)
		{
			AndFlags = and;
			OrFlags  = or;
		}

		public NJD_FLAG Apply(NJD_FLAG flags)
		{
			return UseMaterialFlags ? OrFlags | (AndFlags & flags) : flags;
		}
	}
}