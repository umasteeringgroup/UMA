# Overlay Painter Plugin API v2

Plugin API v2 is the only supported extension API. Plugins never receive `TextureStore`, `EditableTextureTarget`, `RenderTexture`, live layers, or mutable masks.

## Registration contract

Implement `ITexturePaintExtensionV2` through one or more focused extension points:

- `ITexturePaintBrushV2` — modifies color, opacity, size, rotation, or skip state for a standard brush sample.
- `ITexturePaintFilterV2` — reads immutable channel snapshots and submits tile commands.
- `ITexturePaintGeneratorV2` — creates content through tile commands.
- `ITexturePaintBakerV2` — converts immutable snapshots into an in-memory artifact.
- `ITexturePaintImporterV2` — converts an in-memory artifact into tile commands.
- `ITexturePaintExporterV2` — converts immutable snapshots into an in-memory artifact; the host UI owns the destination path.
- `ITexturePaintProceduralMaskV2` — evaluates a read-only procedural mask.

Every plugin supplies a `TexturePaintPluginDescriptor` with:

- A stable lowercase reverse-DNS ID.
- Plugin and API versions.
- Exact capabilities.
- Every channel it may read or write.
- A typed parameter schema with unique IDs and valid ranges.

Discovery rejects duplicate IDs, unsupported API versions, missing channel declarations, capability mismatches, and invalid parameter schemas.

## Safe pixel workflow

Filters, generators, and importers receive `TexturePaintCommandContextV2`:

1. Read source data through `context.source.Get(surfaceId, channel)`. The returned `TexturePaintReadOnlyImage` owns a copy, not a live texture.
2. Periodically check `context.cancellationToken` and report bounded progress.
3. Submit one or more rectangular updates with `WriteTile`.
4. Return from the task. The context is then sealed and cannot accept late background writes.

Example:

```csharp
public Task ExecuteAsync(TexturePaintCommandContextV2 context)
{
    foreach (string surfaceId in context.source.surfaceIds)
    {
        TexturePaintReadOnlyImage source = context.source.Get(surfaceId, TexturePaintChannel.Roughness);
        if (source == null) continue;
        Color[] pixels = source.CopyPixels();
        // Modify the copied data only.
        context.WriteTile(surfaceId, TexturePaintChannel.Roughness,
            new RectInt(0, 0, source.width, source.height), pixels,
            TexturePaintPluginColorSpace.Data, TexturePaintPluginBlend.Replace);
    }
    return Task.CompletedTask;
}
```

The host validates every command before the first mutation, copies submitted buffers, enforces declared channels and bounds, applies structural and painted masks per texel, creates a non-destructive plugin layer, updates only dirty rectangles, recomposes/re-packs logical channels, and records an undoable transaction. Cancellation or any exception removes the entire pending layer set.

Albedo and Emission accept Linear or SRGB payloads and are canonicalized to linear working values. Normal, Metallic, Roughness, AO, and Custom data require `Data`. Normal commands require `Replace` and are vector-normalized by the host. Plugins cannot write directly to packed physical textures.

## Parameters and persistence

Use `TexturePaintPluginParameterDefinition` for Float, Integer, Boolean, Color, String, Texture, and Enum controls. The shared editor renders the schema automatically. Profiles persist in recipe stage state, and committed layers persist plugin ID, plugin version, and parameter JSON in the texture-paint document.

## Budgets, cancellation, and diagnostics

`PluginHost` independently budgets immutable snapshots, queued commands, and artifacts. Command count is capped, payload sizes are checked, and cancellation is checked during snapshot and commit work. The plugin must cooperate during its own asynchronous work.

The Plugins window reports registration, duration, command count, dirty pixels, cancellation, and exception diagnostics. Plugin exceptions are isolated from the stage and do not leave partial output.

See `ExampleBrushPlugin` and `ExampleModelPlugin` for working v2 brush and generator implementations.
