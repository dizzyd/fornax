// Fornax - a mid-game multi-block updraft kiln for Vintage Story
// Copyright (C) 2026 Dave (Dizzy) Smith
//
// This program is free software: you can redistribute it and/or modify it under
// the terms of the GNU Lesser General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option) any
// later version. See COPYING.LESSER, or <https://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Fornax;

/// <summary>
/// Draws the unbuilt part of a kiln as translucent copies of the actual blocks.
///
/// IWorldAccessor.HighlightBlocks can only paint flat colours - there is no textured mode - so a
/// colour-coded highlight tells you a block is missing but not which block it is. This renders
/// each missing position with that block's own mesh instead, so mud brick looks like mud brick
/// and a grate tile looks like a grate tile.
/// </summary>
public class KilnGhostRenderer : IRenderer
{
    private readonly ICoreClientAPI capi;
    private MultiTextureMeshRef meshRef;
    private BlockPos origin;

    private readonly Matrixf modelMat = new Matrixf();

    public double RenderOrder => 0.95;
    public int RenderRange => 128;

    public KilnGhostRenderer(ICoreClientAPI capi)
    {
        this.capi = capi;
        capi.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "fornax-ghost");
    }

    public bool HasGhosts => meshRef != null && !meshRef.Disposed;

    /// <summary>Builds one mesh out of every block that still needs placing.</summary>
    public void ShowGhosts(BlockPos at, IList<BlockPos> positions, IList<Block> blocks)
    {
        Clear();
        if (positions == null || positions.Count == 0) return;

        origin = at.Copy();
        var combined = new MeshData(4, 3, false, true, true, true);

        for (int i = 0; i < positions.Count; i++)
        {
            Block block = blocks[i];
            if (block == null || block.Id == 0) continue;

            MeshData blockMesh;
            try
            {
                blockMesh = capi.TesselatorManager.GetDefaultBlockMesh(block);
            }
            catch (Exception)
            {
                continue;   // a block whose mesh wants a block entity: skip it rather than die
            }

            if (blockMesh == null) continue;

            MeshData copy = blockMesh.Clone();
            copy.Translate(
                positions[i].X - origin.X,
                positions[i].Y - origin.Y,
                positions[i].Z - origin.Z);

            // Flat full lighting, so a ghost standing in an unlit shell is still readable.
            if (copy.Rgba != null)
            {
                for (int j = 0; j < copy.Rgba.Length; j++) copy.Rgba[j] = 255;
            }

            combined.AddMeshData(copy);
        }

        if (combined.VerticesCount == 0) return;

        meshRef = capi.Render.UploadMultiTextureMesh(combined);
    }

    public void Clear()
    {
        meshRef?.Dispose();
        meshRef = null;
    }

    public void OnRenderFrame(float dt, EnumRenderStage stage)
    {
        if (meshRef == null || meshRef.Disposed) return;

        var rend = capi.Render;
        Vec3d camera = capi.World.Player.Entity.CameraPos;

        rend.GlToggleBlend(true);
        rend.GlDisableCullFace();

        IStandardShaderProgram prog = rend.PreparedStandardShader(
            origin.X, origin.Y, origin.Z, new Vec4f(1f, 1f, 1f, 0.55f));

        prog.ExtraGodray = 0;
        prog.ExtraGlow = 0;
        prog.RgbaAmbientIn = rend.AmbientColor;
        prog.RgbaFogIn = rend.FogColor;
        prog.FogMinIn = rend.FogMin;
        prog.FogDensityIn = rend.FogDensity;
        prog.RgbaTint = new Vec4f(1f, 1f, 1f, 0.55f);
        prog.NormalShaded = 1;
        prog.ExtraZOffset = -0.0008f;

        prog.ModelMatrix = modelMat
            .Identity()
            .Translate(origin.X - camera.X, origin.Y - camera.Y, origin.Z - camera.Z)
            .Values;
        prog.ViewMatrix = rend.CameraMatrixOriginf;
        prog.ProjectionMatrix = rend.CurrentProjectionMatrix;

        // The standard shader declares its sampler as "tex" (see assets/game/shaders/standard.fsh).
        // Passing the more commonly quoted "tex2d" throws KeyNotFoundException and takes the
        // client down on the first frame the guide is up.
        rend.RenderMultiTextureMesh(meshRef, "tex");

        prog.ExtraZOffset = 0;
        prog.Stop();

        rend.GlEnableCullFace();
        rend.GlToggleBlend(false);
    }

    public void Dispose()
    {
        capi.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
        Clear();
    }
}
