﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using sadx_model_view.Ninja;
using sadx_model_view.SA1;
using SharpDX;
using SharpDX.D3DCompiler;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SharpDX.Mathematics.Interop;
using Buffer = SharpDX.Direct3D11.Buffer;
using Color = SharpDX.Color;
using Device = SharpDX.Direct3D11.Device;
using MapFlags = SharpDX.Direct3D11.MapFlags;
using Resource = SharpDX.Direct3D11.Resource;

// TODO: Sort meshset by material flags

namespace sadx_model_view
{
	public class Renderer : IDisposable
	{
		private int lastVisibleCount;

		/// <summary>
		/// This is the texture transformation matrix that SADX uses for anything with an environment map.
		/// </summary>
		private static readonly RawMatrix environmentMapTransform = new Matrix(
			-0.5f, 0.0f, 0.0f, 0.0f,
			0.0f, 0.5f, 0.0f, 0.0f,
			0.0f, 0.0f, 1.0f, 0.0f,
			0.5f, 0.5f, 0.0f, 1.0f
		);

		public FlowControl FlowControl;
		public bool EnableAlpha = true;

		private CullMode defaultCullMode = CullMode.None;

		public CullMode DefaultCullMode
		{
			get => defaultCullMode;
			set
			{
				defaultCullMode = value;
				ClearDisplayStates();
			}
		}

		private readonly List<SceneTexture> texturePool = new List<SceneTexture>();

		private readonly Device device;
		private readonly SwapChain swapChain;
		private RenderTargetView backBuffer;
		private Viewport viewPort;
		private Texture2D depthTexture;
		private DepthStencilStateDescription depthDesc;
		private DepthStencilState depthStateRW;
		private DepthStencilState depthStateRO;
		private DepthStencilView depthView;
		private RasterizerState rasterizerState;
		private RasterizerStateDescription rasterizerDescription;
		private readonly Buffer matrixBuffer;
		private readonly Buffer materialBuffer;

		private bool zWrite = true;
		private bool matrixDataChanged;
		private MatrixBuffer lastMatrixData;
		private MatrixBuffer matrixData;

		private readonly MeshsetTree meshTree = new MeshsetTree();
		private readonly Dictionary<NJD_FLAG, DisplayState> displayStates = new Dictionary<NJD_FLAG, DisplayState>();

		public Renderer(int w, int h, IntPtr sceneHandle)
		{
			FlowControl.Reset();

			SetTransform(TransformState.Texture, in environmentMapTransform);

			var desc = new SwapChainDescription
			{
				BufferCount       = 1,
				ModeDescription   = new ModeDescription(w, h, new Rational(1000, 60), Format.R8G8B8A8_UNorm),
				Usage             = Usage.RenderTargetOutput,
				OutputHandle      = sceneHandle,
				IsWindowed        = true,
				SampleDescription = new SampleDescription(1, 0)
			};

			var levels = new FeatureLevel[]
			{
				FeatureLevel.Level_11_1,
				FeatureLevel.Level_11_0,
				FeatureLevel.Level_10_1,
				FeatureLevel.Level_10_0,
			};

#if DEBUG
			const DeviceCreationFlags flag = DeviceCreationFlags.Debug;
#else
			const DeviceCreationFlags flag = DeviceCreationFlags.None;
#endif

			Device.CreateWithSwapChain(DriverType.Hardware, flag, levels, desc, out device, out swapChain);

			if (device.FeatureLevel < FeatureLevel.Level_10_0)
			{
				throw new InsufficientFeatureLevelException(device.FeatureLevel, FeatureLevel.Level_10_0);
			}

			int mtx_size = Matrix.SizeInBytes * 5;
			var bufferDesc = new BufferDescription(mtx_size,
				ResourceUsage.Dynamic, BindFlags.ConstantBuffer, CpuAccessFlags.Write, ResourceOptionFlags.None, mtx_size);

			matrixBuffer = new Buffer(device, bufferDesc);

			// Size must be divisible by 16, so this is just padding.
			int size = Math.Max(ShaderMaterial.SizeInBytes, 80);
			int stride = ShaderMaterial.SizeInBytes + Vector3.SizeInBytes + sizeof(uint);

			bufferDesc = new BufferDescription(size,
				ResourceUsage.Dynamic, BindFlags.ConstantBuffer, CpuAccessFlags.Write, ResourceOptionFlags.None, stride);

			materialBuffer = new Buffer(device, bufferDesc);

			LoadShaders();

			device.ImmediateContext.VertexShader.SetConstantBuffer(0, matrixBuffer);
			device.ImmediateContext.VertexShader.SetConstantBuffer(1, materialBuffer);
			device.ImmediateContext.PixelShader.SetConstantBuffer(1, materialBuffer);
			device.ImmediateContext.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleList;

			RefreshDevice(w, h);
		}

		public void LoadShaders()
		{
			using (var includeMan = new DefaultIncludeHandler())
			{
				vertexShader?.Dispose();
				pixelShader?.Dispose();
				inputLayout?.Dispose();

				CompilationResult vs_result = ShaderBytecode.CompileFromFile("Shaders\\scene_vs.hlsl", "main", "vs_4_0", include: includeMan);

				if (vs_result.HasErrors || !string.IsNullOrEmpty(vs_result.Message))
				{
					throw new Exception(vs_result.Message);
				}

				vertexShader = new VertexShader(device, vs_result.Bytecode);

				CompilationResult ps_result = ShaderBytecode.CompileFromFile("Shaders\\scene_ps.hlsl", "main", "ps_4_0", include: includeMan);

				if (ps_result.HasErrors || !string.IsNullOrEmpty(ps_result.Message))
				{
					throw new Exception(ps_result.Message);
				}

				pixelShader = new PixelShader(device, ps_result.Bytecode);

				var layout = new InputElement[]
				{
					new InputElement("POSITION", 0, Format.R32G32B32_Float, InputElement.AppendAligned, 0, InputClassification.PerVertexData, 0),
					new InputElement("NORMAL",   0, Format.R32G32B32_Float, InputElement.AppendAligned, 0, InputClassification.PerVertexData, 0),
					new InputElement("COLOR",    0, Format.R8G8B8A8_UNorm,  InputElement.AppendAligned, 0, InputClassification.PerVertexData, 0),
					new InputElement("TEXCOORD", 0, Format.R32G32_Float,    InputElement.AppendAligned, 0, InputClassification.PerVertexData, 0)
				};

				inputLayout = new InputLayout(device, vs_result.Bytecode, layout);

				device.ImmediateContext.VertexShader.Set(vertexShader);
				device.ImmediateContext.PixelShader.Set(pixelShader);
				device.ImmediateContext.InputAssembler.InputLayout = inputLayout;
			}
		}

		public void Clear()
		{
			if (device == null)
			{
				return;
			}

			meshTree.Clear();

			device.ImmediateContext.Rasterizer.State = rasterizerState;
			device.ImmediateContext.ClearRenderTargetView(backBuffer, new RawColor4(0.0f, 1.0f, 1.0f, 1.0f));

#if REVERSE_Z
			device.ImmediateContext.ClearDepthStencilView(depthView, DepthStencilClearFlags.Depth, 0.0f, 0);
#else
			device.ImmediateContext.ClearDepthStencilView(depthView, DepthStencilClearFlags.Depth, 1.0f, 0);
#endif
		}

		public void Draw(Camera camera, NJS_OBJECT obj)
		{
			while (!(obj is null))
			{
				MatrixStack.Push();
				obj.PushTransform();
				RawMatrix m = MatrixStack.Peek();
				SetTransform(TransformState.World, in m);

				if (!obj.SkipDraw && obj.Model != null)
				{
					Draw(camera, obj.Model);
				}

				if (!obj.SkipChildren)
				{
					Draw(camera, obj.Child);
				}

				MatrixStack.Pop();
				obj = obj.Sibling;
			}
		}

		private static readonly NJS_MATERIAL nullMaterial = new NJS_MATERIAL();

		public void Draw(Camera camera, NJS_MODEL model)
		{
			foreach (NJS_MESHSET set in model.meshsets)
			{
				meshTree.Enqueue(this, camera, model, set);
			}
		}

		private Buffer lastVertexBuffer;
		private BlendState lastBlend;
		private RasterizerState lastRasterizerState;
		private SamplerState lastSamplerState;

		private void DrawSet(Camera camera, NJS_MODEL parent, NJS_MESHSET set)
		{
			ushort materialId = set.MaterialId;
			List<NJS_MATERIAL> mats = parent.mats;
			NJS_MATERIAL njMaterial = mats.Count > 0 && materialId < mats.Count ? mats[materialId] : nullMaterial;

			FlowControl flowControl = FlowControl;

			if (texturePool.Count < 1)
			{
				if (!FlowControl.UseMaterialFlags)
				{
					FlowControl.Reset();
					FlowControl.UseMaterialFlags = true;
				}

				FlowControl.Set(FlowControl.AndFlags & ~NJD_FLAG.UseTexture, FlowControl.OrFlags);
			}

			ShaderMaterial shaderMaterial = parent.GetSADXMaterial(this, njMaterial);
			SetShaderMaterial(in shaderMaterial, camera);

			DisplayState state = GetSADXDisplayState(njMaterial);

			FlowControl = flowControl;

			if (state.Blend != lastBlend)
			{
				device.ImmediateContext.OutputMerger.SetBlendState(state.Blend);
				lastBlend = state.Blend;
			}

			if (state.Sampler != lastSamplerState)
			{
				device.ImmediateContext.PixelShader.SetSampler(0, state.Sampler);
				lastSamplerState = state.Sampler;
			}

			if (state.Raster != lastRasterizerState)
			{
				device.ImmediateContext.Rasterizer.State = state.Raster;
				lastRasterizerState = state.Raster;
			}

			if (matrixDataChanged && lastMatrixData != matrixData)
			{
				device.ImmediateContext.MapSubresource(matrixBuffer, MapMode.WriteDiscard,
					MapFlags.None, out DataStream stream);

				using (stream)
				{
					Matrix wvMatrixInvT = matrixData.World * matrixData.View;
					Matrix.Invert(ref wvMatrixInvT, out wvMatrixInvT);

					stream.Write(matrixData.World);
					stream.Write(matrixData.View);
					stream.Write(matrixData.Projection);
					stream.Write(wvMatrixInvT);
					stream.Write(matrixData.Texture);
				}

				device.ImmediateContext.UnmapSubresource(matrixBuffer, 0);
				lastMatrixData = matrixData;
			}

			if (parent.VertexBuffer != lastVertexBuffer)
			{
				var binding = new VertexBufferBinding(parent.VertexBuffer, Vertex.SizeInBytes, 0);
				device.ImmediateContext.InputAssembler.SetVertexBuffers(0, binding);
				lastVertexBuffer = parent.VertexBuffer;
			}

			device.ImmediateContext.InputAssembler.SetIndexBuffer(set.IndexBuffer, Format.R16_UInt, 0);
			device.ImmediateContext.DrawIndexed(set.IndexCount, 0, 0);
		}

		public void Present(Camera camera)
		{
			int visibleCount = 0;
			zWrite = true;

			foreach (var e in meshTree.OpaqueSets)
			{
				++visibleCount;
				DrawMeshsetQueueElement(camera, e);
			}

			if (EnableAlpha)
			{
				// First draw with depth writes enabled & alpha threshold (in shader)
				foreach (var e in meshTree.AlphaSets)
				{
					++visibleCount;
					DrawMeshsetQueueElement(camera, e);
				}

				// Now draw with depth writes disabled
				device.ImmediateContext.OutputMerger.SetDepthStencilState(depthStateRO);
				zWrite = false;

				foreach (var e in meshTree.AlphaSets)
				{
					DrawMeshsetQueueElement(camera, e);
				}

				device.ImmediateContext.OutputMerger.SetDepthStencilState(depthStateRW);
				zWrite = true;
			}

			meshTree.Clear();

			swapChain.Present(0, 0);

			if (!MatrixStack.Empty)
			{
				throw new Exception("Matrix stack still contains data");
			}

			if (visibleCount != lastVisibleCount)
			{
				lastVisibleCount = visibleCount;
				Debug.WriteLine(visibleCount);
			}
		}

		private void DrawMeshsetQueueElement(Camera camera, MeshsetQueueElement e)
		{
			FlowControl = e.FlowControl;

			RawMatrix m = e.Transform;
			SetTransform(TransformState.World, in m);

			DrawSet(camera, e.Model, e.Set);
		}

		private void CreateRenderTarget()
		{
			using (var pBackBuffer = Resource.FromSwapChain<Texture2D>(swapChain, 0))
			{
				backBuffer?.Dispose();
				backBuffer = new RenderTargetView(device, pBackBuffer);
			}

			device.ImmediateContext.OutputMerger.SetRenderTargets(backBuffer);
		}

		private void SetViewPort(int x, int y, int width, int height)
		{
			viewPort.MinDepth = 0f;
			viewPort.MaxDepth = 1f;

			Viewport vp = viewPort;

			vp.X = x;
			vp.Y = y;
			vp.Width = width;
			vp.Height = height;

			if (vp == viewPort)
			{
				return;
			}

			viewPort = vp;
			device.ImmediateContext.Rasterizer.SetViewport(viewPort);
		}

		public void RefreshDevice(int w, int h)
		{
			backBuffer?.Dispose();
			swapChain?.ResizeBuffers(1, w, h, Format.Unknown, 0);
			SetViewPort(0, 0, w, h);

			CreateRenderTarget();
			CreateRasterizerState();
			CreateDepthStencil(w, h);
		}

		private void CreateDepthStencil(int w, int h)
		{
			// TODO: shader resource?

			var depthBufferDesc = new Texture2DDescription
			{
				Width             = w,
				Height            = h,
				MipLevels         = 1,
				ArraySize         = 1,
				Format            = Format.D24_UNorm_S8_UInt,
				SampleDescription = new SampleDescription(1, 0),
				Usage             = ResourceUsage.Default,
				BindFlags         = BindFlags.DepthStencil,
				CpuAccessFlags    = CpuAccessFlags.None,
				OptionFlags       = ResourceOptionFlags.None
			};

			depthTexture?.Dispose();
			depthTexture = new Texture2D(device, depthBufferDesc);

			depthDesc = new DepthStencilStateDescription
			{
				IsDepthEnabled  = true,
				DepthWriteMask  = DepthWriteMask.All,
#if REVERSE_Z
				DepthComparison = Comparison.Greater,
#else
				DepthComparison = Comparison.Less,
#endif

				FrontFace = new DepthStencilOperationDescription
				{
					FailOperation = StencilOperation.Keep,
					PassOperation = StencilOperation.Keep,
					Comparison    = Comparison.Always
				},

				BackFace = new DepthStencilOperationDescription
				{
					FailOperation = StencilOperation.Keep,
					PassOperation = StencilOperation.Keep,
					Comparison    = Comparison.Always
				}
			};

			depthStateRW?.Dispose();
			depthStateRW = new DepthStencilState(device, depthDesc);
			
			depthDesc.DepthWriteMask = DepthWriteMask.Zero;
			depthStateRO?.Dispose();
			depthStateRO = new DepthStencilState(device, depthDesc);

			var depthViewDesc = new DepthStencilViewDescription
			{
				Format    = Format.D24_UNorm_S8_UInt,
				Dimension = DepthStencilViewDimension.Texture2D,
				Texture2D = new DepthStencilViewDescription.Texture2DResource
				{
					MipSlice = 0
				}
			};

			depthView?.Dispose();
			depthView = new DepthStencilView(device, depthTexture, depthViewDesc);

			device?.ImmediateContext.OutputMerger.SetTargets(depthView, backBuffer);
			device?.ImmediateContext.OutputMerger.SetDepthStencilState(depthStateRW);
		}

		public void Draw(LandTable landTable, Camera camera)
		{
			if (landTable == null)
			{
				return;
			}

			foreach (Col col in landTable.ColList)
			{
				if ((col.Flags & ColFlags.Visible) != 0)
				{
					Draw(camera, col.Object);
				}
			}
		}

		private void CreateRasterizerState()
		{
			rasterizerDescription = new RasterizerStateDescription
			{
				IsAntialiasedLineEnabled = false,
				CullMode                 = DefaultCullMode,
				DepthBias                = 0,
				DepthBiasClamp           = 0.0f,
				IsDepthClipEnabled       = true,
				FillMode                 = FillMode.Solid,
				IsFrontCounterClockwise  = false,
				IsMultisampleEnabled     = false,
				IsScissorEnabled         = false,
				SlopeScaledDepthBias     = 0.0f
			};

			rasterizerState?.Dispose();
			rasterizerState = new RasterizerState(device, rasterizerDescription);

			device.ImmediateContext.Rasterizer.State = rasterizerState;
		}

		private void CopyToTexture(Texture2D texture, Bitmap bitmap, int level)
		{
			BitmapData bmpData = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
			var buffer = new byte[bmpData.Stride * bitmap.Height];
			Marshal.Copy(bmpData.Scan0, buffer, 0, buffer.Length);
			bitmap.UnlockBits(bmpData);

			device.ImmediateContext.UpdateSubresource(buffer, texture, level, 4 * bitmap.Width, 4 * bitmap.Height);
		}

		public void CreateTextureFromBitMap(Bitmap bitmap, Bitmap[] mipmaps, int levels)
		{
			var texDesc = new Texture2DDescription
			{
				ArraySize         = 1,
				BindFlags         = BindFlags.ShaderResource,
				CpuAccessFlags    = CpuAccessFlags.Write,
				Format            = Format.B8G8R8A8_UNorm,
				Width             = bitmap.Width,
				Height            = bitmap.Height,
				MipLevels         = levels,
				Usage             = ResourceUsage.Default,
				SampleDescription = new SampleDescription(1, 0)
			};

			var texture = new Texture2D(device, texDesc);

			if (mipmaps?.Length > 0)
			{
				for (int i = 0; i < levels; i++)
				{
					CopyToTexture(texture, mipmaps[i], i);
				}
			}
			else
			{
				CopyToTexture(texture, bitmap, 0);
			}

			var pair = new SceneTexture(texture, new ShaderResourceView(device, texture));
			texturePool.Add(pair);
		}

		public void ClearTexturePool()
		{
			foreach (SceneTexture texture in texturePool)
			{
				texture.Dispose();
			}

			texturePool.Clear();
			SetTexture(0, -1);
		}

		public Buffer CreateVertexBuffer(IReadOnlyCollection<Vertex> vertices)
		{
			int vertexSize = vertices.Count * Vertex.SizeInBytes;

			var desc = new BufferDescription(vertexSize, BindFlags.VertexBuffer, ResourceUsage.Immutable);

			using (var stream = new DataStream(vertexSize, true, true))
			{
				foreach (Vertex v in vertices)
				{
					stream.Write(v.position.X);
					stream.Write(v.position.Y);
					stream.Write(v.position.Z);

					stream.Write(v.normal.X);
					stream.Write(v.normal.Y);
					stream.Write(v.normal.Z);

					RawColorBGRA color = v.diffuse == null ? Color.White : v.diffuse.Value;

					stream.Write(color.B);
					stream.Write(color.G);
					stream.Write(color.R);
					stream.Write(color.A);

					RawVector2 uv = v.uv == null ? (RawVector2)Vector2.Zero : v.uv.Value;

					stream.Write(uv.X);
					stream.Write(uv.Y);
				}

				if (stream.RemainingLength != 0)
				{
					throw new Exception("Failed to fill vertex buffer.");
				}

				stream.Position = 0;
				return new Buffer(device, stream, desc);
			}
		}

		public Buffer CreateIndexBuffer(IEnumerable<short> indices, int sizeInBytes)
		{
			var desc = new BufferDescription(sizeInBytes, BindFlags.IndexBuffer, ResourceUsage.Immutable);

			using (var stream = new DataStream(sizeInBytes, true, true))
			{
				foreach (short i in indices)
				{
					stream.Write(i);
				}

				if (stream.RemainingLength != 0)
				{
					throw new Exception("Failed to fill index buffer.");
				}

				stream.Position = 0;
				return new Buffer(device, stream, desc);
			}
		}

		public void SetTransform(TransformState state, in RawMatrix rawMatrix)
		{
			switch (state)
			{
				case TransformState.World:
					matrixData.World = rawMatrix;
					break;
				case TransformState.View:
					matrixData.View = rawMatrix;
					break;
				case TransformState.Projection:
#if REVERSE_Z
					var a = new Matrix(
						1f, 0f,  0f, 0f,
						0f, 1f,  0f, 0f,
						0f, 0f, -1f, 0f,
						0f, 0f,  1f, 1f
					);

					matrixData.Projection = rawMatrix * a;
#else
					matrixData.Projection = rawMatrix;
#endif
					break;
				case TransformState.Texture:
					matrixData.Texture = rawMatrix;
					break;
				default:
					throw new ArgumentOutOfRangeException(nameof(state), state, null);
			}

			matrixDataChanged = true;
		}

		private SceneTexture lastTexture;
		public void SetTexture(int sampler, int textureIndex)
		{
			if (textureIndex >= 0 && textureIndex < texturePool.Count)
			{
				SceneTexture texture = texturePool[textureIndex];

				if (ReferenceEquals(texture, lastTexture))
				{
					return;
				}

				device.ImmediateContext.PixelShader.SetShaderResource(sampler, texture.ShaderResource);
				lastTexture = texture;
			}
			else if (lastTexture != null)
			{
				lastTexture = null;
				device.ImmediateContext.PixelShader.SetShaderResource(sampler, null);
			}
		}

		// TODO: renderer interface to handle SADX/SA1/SA2 renderers

		private static readonly BlendOption[] blendModes =
		{
			BlendOption.Zero,
			BlendOption.One,
			BlendOption.SourceColor,
			BlendOption.InverseSourceColor,
			BlendOption.SourceAlpha,
			BlendOption.InverseSourceAlpha,
			BlendOption.DestinationAlpha,
			BlendOption.InverseDestinationAlpha,
		};

		// TODO: generate in bulk
		public DisplayState GetSADXDisplayState(NJS_MATERIAL material)
		{
			// Not implemented in SADX:
			// - NJD_FLAG.Pick
			// - NJD_FLAG.UseAnisotropic
			// - NJD_FLAG.UseFlat

			const NJD_FLAG state_mask = NJD_FLAG.ClampU | NJD_FLAG.ClampV | NJD_FLAG.FlipU | NJD_FLAG.FlipV
			                            | NJD_FLAG.DoubleSide | NJD_FLAG.UseAlpha;

			NJD_FLAG flags = FlowControl.Apply(material.attrflags) & state_mask;

			if (displayStates.TryGetValue(flags, out DisplayState state))
			{
				return state;
			}

			var samplerDesc = new SamplerStateDescription
			{
				AddressW           = TextureAddressMode.Wrap,
				Filter             = Filter.MinMagMipLinear,
				MinimumLod         = -float.MaxValue,
				MaximumLod         = float.MaxValue,
				MaximumAnisotropy  = 1,
				ComparisonFunction = Comparison.Never
			};

			if ((flags & (NJD_FLAG.ClampU | NJD_FLAG.FlipU)) == (NJD_FLAG.ClampU | NJD_FLAG.FlipU))
			{
				samplerDesc.AddressU = TextureAddressMode.MirrorOnce;
			}
			else if ((flags & NJD_FLAG.ClampU) != 0)
			{
				samplerDesc.AddressU = TextureAddressMode.Clamp;
			}
			else if ((flags & NJD_FLAG.FlipU) != 0)
			{
				samplerDesc.AddressU = TextureAddressMode.Mirror;
			}
			else
			{
				samplerDesc.AddressU = TextureAddressMode.Wrap;
			}

			if ((flags & (NJD_FLAG.ClampV | NJD_FLAG.FlipV)) == (NJD_FLAG.ClampV | NJD_FLAG.FlipV))
			{
				samplerDesc.AddressV = TextureAddressMode.MirrorOnce;
			}
			else if ((flags & NJD_FLAG.ClampV) != 0)
			{
				samplerDesc.AddressV = TextureAddressMode.Clamp;
			}
			else if ((flags & NJD_FLAG.FlipV) != 0)
			{
				samplerDesc.AddressV = TextureAddressMode.Mirror;
			}
			else
			{
				samplerDesc.AddressV = TextureAddressMode.Wrap;
			}

			var sampler = new SamplerState(device, samplerDesc) { DebugName = $"Sampler: {flags.ToString()}" };

			// Base it off of the default rasterizer state.
			RasterizerStateDescription rasterDesc = rasterizerDescription;

			rasterDesc.CullMode = (flags & NJD_FLAG.DoubleSide) != 0 ? CullMode.None : DefaultCullMode;

			var raster = new RasterizerState(device, rasterDesc) { DebugName = $"Rasterizer: {flags.ToString()}" };

			var blendDesc = new BlendStateDescription();
			ref RenderTargetBlendDescription rt = ref blendDesc.RenderTarget[0];

			rt.IsBlendEnabled        = (flags & NJD_FLAG.UseAlpha) != 0;
			rt.SourceBlend           = blendModes[material.SourceBlend];
			rt.DestinationBlend      = blendModes[material.DestinationBlend];
			rt.BlendOperation        = BlendOperation.Add;
			rt.SourceAlphaBlend      = blendModes[material.SourceBlend];
			rt.DestinationAlphaBlend = blendModes[material.DestinationBlend];
			rt.AlphaBlendOperation   = BlendOperation.Add;
			rt.RenderTargetWriteMask = ColorWriteMaskFlags.All;

			var blend = new BlendState(device, blendDesc) { DebugName = $"Blend: {flags.ToString()}" };

			var result = new DisplayState(sampler, raster, blend);
			displayStates[flags] = result;

			return result;
		}

		private ShaderMaterial lastMaterial;
		private VertexShader vertexShader;
		private PixelShader pixelShader;
		private InputLayout inputLayout;

		private bool lastZwrite;
		public void SetShaderMaterial(in ShaderMaterial material, Camera camera)
		{
			if (material == lastMaterial && zWrite == lastZwrite)
			{
				return;
			}

			lastMaterial = material;
			lastZwrite = zWrite;

			device.ImmediateContext.MapSubresource(materialBuffer, MapMode.WriteDiscard, MapFlags.None, out DataStream stream);
			using (stream)
			{
				stream.Write(material.Diffuse);
				stream.Write(material.Specular);
				stream.Write(material.Exponent);
				stream.Write(material.UseLight ? 1 : 0);
				stream.Write(material.UseAlpha ? 1 : 0);
				stream.Write(material.UseEnv ? 1 : 0);
				stream.Write(material.UseTexture ? 1 : 0);
				stream.Write(material.UseSpecular ? 1 : 0);
				stream.Write(zWrite ? 1 : 0);
				stream.Write(camera.Position);
			}
			device.ImmediateContext.UnmapSubresource(materialBuffer, 0);
		}

		public void Dispose()
		{
			swapChain?.Dispose();
			backBuffer?.Dispose();
			depthTexture?.Dispose();
			depthStateRW?.Dispose();
			depthStateRO?.Dispose();
			depthView?.Dispose();
			rasterizerState?.Dispose();
			matrixBuffer?.Dispose();
			materialBuffer?.Dispose();
			lastVertexBuffer?.Dispose();
			lastBlend?.Dispose();
			lastRasterizerState?.Dispose();
			lastSamplerState?.Dispose();
			lastTexture?.Dispose();
			vertexShader?.Dispose();
			pixelShader?.Dispose();
			inputLayout?.Dispose();

			ClearTexturePool();
			ClearDisplayStates();

#if DEBUG
			using (var debug = new DeviceDebug(device))
			{
				debug.ReportLiveDeviceObjects(ReportingLevel.Summary);
			}
#endif

			device?.Dispose();
		}

		private void ClearDisplayStates()
		{
			foreach (KeyValuePair<NJD_FLAG, DisplayState> i in displayStates)
			{
				i.Value.Dispose();
			}

			displayStates.Clear();
		}
	}

	internal class InsufficientFeatureLevelException : Exception
	{
		public readonly FeatureLevel SupportedLevel;
		public readonly FeatureLevel TargetLevel;

		public InsufficientFeatureLevelException(FeatureLevel supported, FeatureLevel target)
		{
			SupportedLevel = supported;
			TargetLevel = target;
		}
	}
}
