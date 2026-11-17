#!/bin/bash
# Avvia Dolphin standalone (AppImage senza FUSE) per ES-DE.
# Impostazioni grafiche passate con -C; gli argomenti di ES-DE (-b -e <rom>) vengono inoltrati.
exec /Applications/dolphin-emu.AppImage --appimage-extract-and-run \
  -C Dolphin.Display.Fullscreen=True \
  -C Dolphin.Core.GFXBackend=Vulkan \
  -C Graphics.Settings.InternalResolution=2 \
  -C Graphics.Settings.wideScreenHack=True \
  -C Graphics.Settings.AspectRatio=1 \
  -C Graphics.Settings.ShaderCompilationMode=1 \
  -C Graphics.Settings.WaitForShadersBeforeStarting=True \
  -C Graphics.Settings.ShowFPS=True \
  "$@"
