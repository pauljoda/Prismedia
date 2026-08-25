# Nix Development Environment

Prismedia's flake provides a pinned development toolchain for Linux and Apple
silicon macOS. It supplies Node.js, the exact pnpm version declared by the
workspace, .NET 10, PostgreSQL client tools, the Jellyfin ffmpeg/ffprobe fork,
the Docker CLI with Compose and Buildx, Python scraper dependencies,
Playwright's matching Chromium build, and the normal shell utilities used by
repository scripts.

`x86_64-linux` is the primary platform and is listed first in every flake
output. `aarch64-linux` and `aarch64-darwin` are supported as secondary
development platforms.

The flake is the development environment. Prismedia's supported production
artifact remains the unified Docker image.

## Fresh Clone

Nix and a reachable Docker daemon are the only host prerequisites. On NixOS,
the module below configures both. On macOS or another Linux distribution,
install Nix with flakes enabled and start Docker Desktop or another compatible
daemon.

```bash
git clone https://github.com/pauljoda/Prismedia.git
cd Prismedia
nix develop
prismedia-setup
prismedia-doctor
```

`prismedia-setup` installs the pnpm lockfile, restores the repository's local
.NET tools, and restores NuGet packages. It is safe to run again after either
dependency graph changes. `prismedia-doctor` checks versions, media and database
tools, Python scraper imports, the pinned Playwright browser, and Docker daemon
access.

The committed `.envrc` supports automatic shell activation for direnv users:

```bash
direnv allow
```

Entering the shell never installs workspace packages or changes repository
files. This keeps shell activation deterministic; the explicit setup command is
the only dependency-restoration step.

## NixOS Host Configuration

Add the Prismedia input and development module to the flake that owns the host.
Replace `workstation` and `alice` with the real host and user names.

```nix
{
  inputs = {
    nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";
    prismedia.url = "github:pauljoda/Prismedia";
  };

  outputs =
    {
      nixpkgs,
      prismedia,
      ...
    }:
    {
      nixosConfigurations.workstation = nixpkgs.lib.nixosSystem {
        system = "x86_64-linux";
        modules = [
          prismedia.nixosModules.development
          {
            prismedia.development = {
              enable = true;
              gpu.enable = true;
              users = [ "alice" ];
            };
          }
        ];
      };
    };
}
```

The module:

- enables `nix-command` and `flakes`;
- enables Docker and adds only the listed existing users to its group;
- enables `nix-ld` so upstream native pnpm binaries such as esbuild run on
  NixOS without patching `node_modules`;
- optionally enables the graphics stack and gives listed users access to
  render/video devices; and
- enables direnv with nix-direnv caching.

Membership in the Docker group is root-equivalent. Leave `users` empty and use
a separately configured rootless or remote daemon if that access is not
acceptable. Host features can be disabled independently:

```nix
prismedia.development = {
  enable = true;
  users = [ ];
  docker.enable = false;
  direnv.enable = false;
  gpu.enable = false;
};
```

If the existing NixOS installation has not enabled flakes yet, add this directly
to its current configuration and rebuild once before adding a flake input:

```nix
nix.settings.experimental-features = [ "nix-command" "flakes" ];
```

Log out and back in after first joining the Docker group.

## Hardware Transcoding

Prismedia already exposes Auto, VA-API, Intel Quick Sync, and NVIDIA NVENC
encoding profiles in Settings and supplies the corresponding ffmpeg arguments.
No application change is needed. The flake supplies Jellyfin FFmpeg, matching
the fork used by the unified production image. Its patch release comes from the
pinned nixpkgs revision, and the doctor rejects a non-Jellyfin build. On Linux,
the flake check also verifies that it contains the `h264_vaapi`, `h264_qsv`, and
`h264_nvenc` encoders.

Setting `prismedia.development.gpu.enable = true` enables NixOS graphics support
and adds the module's listed users to the `render` and `video` groups. The host
must still select the driver for its hardware. For example, a current Intel host
can add:

```nix
hardware.graphics.extraPackages = with pkgs; [
  intel-media-driver
  vpl-gpu-rt
];
```

AMD hardware normally uses the Mesa drivers enabled by `hardware.graphics`.
NVIDIA hardware requires the host's normal NixOS NVIDIA configuration, including
`services.xserver.videoDrivers`, `hardware.nvidia`, and unfree-package policy as
appropriate for that GPU. Enable `hardware.nvidia-container-toolkit` only when
the worker itself runs in a container and needs NVIDIA device passthrough.

After rebuilding and logging back in, validate the host before selecting the
matching profile in Prismedia:

```bash
ffmpeg -hide_banner -encoders | grep -E 'h264_(vaapi|qsv|nvenc)'
vainfo --display drm --device /dev/dri/renderD128  # VA-API/QSV hosts
nvidia-smi                                         # NVIDIA hosts
```

Encoder presence proves that ffmpeg supports the API; `vainfo` or `nvidia-smi`
proves the host driver can see the device. If the worker runs in Docker, the
render device (or NVIDIA runtime) must also be passed into that container. Auto
currently detects a configured VA-API render device on Linux; choose QSV or
NVENC explicitly for those paths.

## Run The Development Stack

From `nix develop`, use the repository's canonical stack commands in separate
terminals:

```bash
docker compose -f infra/docker/docker-compose.yml up -d postgres
pnpm --filter @prismedia/web-svelte dev
dotnet run --project apps/backend/src/Prismedia.Api/Prismedia.Api.csproj
dotnet run --project apps/backend/src/Prismedia.Worker/Prismedia.Worker.csproj
```

Open [http://localhost:8008](http://localhost:8008). The flake does not set
`DOCKER_HOST`, so rootless and remote Docker configurations are preserved.

The shell exports the exact executable paths the backend accepts for ffmpeg,
ffprobe, `pg_dump`, and `pg_restore`. It also sets
`PLAYWRIGHT_BROWSERS_PATH` and prevents Playwright from downloading a second,
incompatible browser build. Its MSBuild environment also symlinks immutable
reference assemblies from the Nix store instead of copying them into writable
build output, which is required by the API test project's preserved compilation
context.

## Validate And Maintain The Flake

Run the complete flake gate after changing Nix files:

```bash
nix flake check
nix fmt -- --check
```

The checks format and lint the Nix/shell files, exercise every supplied tool,
verify Jellyfin FFmpeg and its Linux hardware encoders, import the Python
scraper packages, verify the Playwright browser revision, and evaluate the
NixOS host module. The lock file makes normal shell entry reproducible across
machines.

Update the general package set deliberately:

```bash
nix flake update nixpkgs
nix flake check
```

The flake also asserts its Jellyfin FFmpeg package version. Review that version
alongside the production image's Jellyfin FFmpeg pin before accepting a
nixpkgs update; the check intentionally fails until the assertion is updated.

The Playwright nixpkgs input is intentionally pinned to the repository's
`@playwright/test` version. When upgrading Playwright, update that input to a
nixpkgs revision carrying the same version. The flake assertion and toolchain
check fail on version drift.

Useful direct commands are also exposed without entering an interactive shell:

```bash
nix run .#setup
nix run .#doctor
```
