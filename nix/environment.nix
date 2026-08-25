{
  system,
  nixpkgs,
  nixpkgs-playwright,
}:
let
  pkgs = import nixpkgs { inherit system; };
  playwrightPkgs = import nixpkgs-playwright { inherit system; };
  lib = pkgs.lib;

  nodeMajor = "22";
  pnpmVersion = "10.30.3";
  playwrightVersion = "1.60.0";
  jellyfinFfmpegVersion = "7.1.4-3";

  nodejs = pkgs.nodejs_22;
  pnpm = pkgs.pnpm_10.override {
    version = pnpmVersion;
    hash = "sha256-/wpyFA9qbWbAsoT2yVYK/2BVGOKMKa6sJfsmK3QzFYg=";
    nodejs-slim = pkgs.nodejs-slim_22;
  };
  dotnetSdk = pkgs.dotnet-sdk_10;
  jellyfinFfmpeg =
    assert pkgs.jellyfin-ffmpeg.version == jellyfinFfmpegVersion;
    pkgs.jellyfin-ffmpeg;

  stashappTools = pkgs.python312Packages.buildPythonPackage rec {
    pname = "stashapp-tools";
    version = "0.2.59";
    pyproject = true;

    src = pkgs.fetchPypi {
      pname = "stashapp-tools";
      inherit version;
      hash = "sha256-Y52YueWHp8C2FsnJ01YMBkz4O2z4d7RBeCswWGr8SjY=";
    };

    build-system = [ pkgs.python312Packages.setuptools ];
    dependencies = [ pkgs.python312Packages.requests ];
    pythonImportsCheck = [ "stashapi" ];
  };

  python = pkgs.python312.withPackages (pythonPackages: [
    pythonPackages.beautifulsoup4
    pythonPackages.cloudscraper
    pythonPackages.lxml
    pythonPackages.python-dateutil
    pythonPackages.requests
    stashappTools
  ]);

  playwrightBrowsers =
    assert playwrightPkgs.playwright.version == playwrightVersion;
    playwrightPkgs.playwright.selectBrowsers {
      withChromium = true;
      withChromiumHeadlessShell = true;
      withFirefox = false;
      withWebkit = false;
      withFfmpeg = true;
    };

  toolchainPackages = [
    pkgs.bashInteractive
    pkgs.cacert
    pkgs.coreutils
    pkgs.curl
    pkgs.docker-client
    dotnetSdk
    jellyfinFfmpeg
    pkgs.findutils
    pkgs.git
    pkgs.gnugrep
    pkgs.gnused
    pkgs.gnutar
    pkgs.gzip
    pkgs.jq
    nodejs
    pnpm
    pkgs.postgresql_16
    python
    pkgs.shellcheck
    pkgs.unzip
  ]
  ++ lib.optionals pkgs.stdenv.hostPlatform.isLinux [ pkgs.libva-utils ];

  shellVariables = {
    # Avoid copying immutable Nix store reference assemblies into writable
    # build outputs. This is the SDK-supported equivalent of Nix's normal
    # symlink-based environment composition.
    CreateSymbolicLinksForCopyFilesToOutputDirectoryIfPossible = "true";
    DOTNET_CLI_TELEMETRY_OPTOUT = "1";
    DOTNET_NOLOGO = "1";
    DOTNET_ROOT = "${dotnetSdk}/share/dotnet";
    PLAYWRIGHT_BROWSERS_PATH = "${playwrightBrowsers}";
    PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD = "1";
    PRISMEDIA_FFMPEG_PATH = lib.getExe jellyfinFfmpeg;
    PRISMEDIA_FFPROBE_PATH = lib.getExe' jellyfinFfmpeg "ffprobe";
    PRISMEDIA_JELLYFIN_FFMPEG_VERSION = jellyfinFfmpegVersion;
    PRISMEDIA_NODE_MAJOR = nodeMajor;
    PRISMEDIA_PG_DUMP_PATH = lib.getExe' pkgs.postgresql_16 "pg_dump";
    PRISMEDIA_PG_RESTORE_PATH = lib.getExe' pkgs.postgresql_16 "pg_restore";
    PRISMEDIA_PLAYWRIGHT_VERSION = playwrightVersion;
  };

  exportShellVariables = lib.concatStringsSep "\n" (
    lib.mapAttrsToList (name: value: "export ${name}=${lib.escapeShellArg value}") shellVariables
  );

  doctor = pkgs.writeShellApplication {
    name = "prismedia-doctor";
    runtimeInputs = toolchainPackages;
    text = ''
      ${exportShellVariables}
      ${builtins.readFile ./scripts/doctor.sh}
    '';
  };

  setup = pkgs.writeShellApplication {
    name = "prismedia-setup";
    runtimeInputs = [
      dotnetSdk
      pkgs.git
      nodejs
      pnpm
    ];
    text = builtins.readFile ./scripts/setup.sh;
  };

  devTools = pkgs.symlinkJoin {
    name = "prismedia-dev-tools";
    paths = [
      doctor
      setup
    ];
  };

  devShell = pkgs.mkShell (
    shellVariables
    // {
      packages = toolchainPackages ++ [
        doctor
        setup
      ];

      shellHook = ''
        if [[ -t 1 ]]; then
          echo "Prismedia development shell"
          echo "  Run prismedia-setup once, then prismedia-doctor to verify the host."
        fi
      '';
    }
  );

  toolchainCheck =
    pkgs.runCommand "prismedia-toolchain-smoke"
      (
        shellVariables
        // {
          nativeBuildInputs = toolchainPackages;
        }
      )
      ''
        bash ${./checks/toolchain-smoke.sh}
        touch "$out"
      '';

  scriptsCheck =
    pkgs.runCommand "prismedia-nix-shellcheck" { nativeBuildInputs = [ pkgs.shellcheck ]; }
      ''
        shellcheck \
          ${./checks/toolchain-smoke.sh} \
          ${./scripts/doctor.sh} \
          ${./scripts/setup.sh}
        touch "$out"
      '';

  formatCheck = pkgs.runCommand "prismedia-nix-format" { nativeBuildInputs = [ pkgs.nixfmt ]; } ''
    nixfmt --check ${../flake.nix} ${./environment.nix} ${./nixos-module.nix}
    touch "$out"
  '';

  nixosModuleCheck =
    let
      evaluated = nixpkgs.lib.nixosSystem {
        inherit system;
        modules = [
          ./nixos-module.nix
          {
            prismedia.development = {
              enable = true;
              gpu.enable = true;
              users = [ "prismedia" ];
            };
            users.users.prismedia.isNormalUser = true;
            system.stateVersion = "26.05";
          }
        ];
      };
      dockerEnabled = evaluated.config.virtualisation.docker.enable;
      graphicsEnabled = evaluated.config.hardware.graphics.enable;
      nixLdEnabled = evaluated.config.programs.nix-ld.enable;
      userGroups = evaluated.config.users.users.prismedia.extraGroups;
    in
    pkgs.runCommand "prismedia-nixos-module-evaluation" { } ''
      [[ ${lib.escapeShellArg (lib.boolToString dockerEnabled)} == true ]]
      [[ ${lib.escapeShellArg (lib.boolToString graphicsEnabled)} == true ]]
      [[ ${lib.escapeShellArg (lib.boolToString nixLdEnabled)} == true ]]
      [[ ${lib.escapeShellArg (lib.boolToString (builtins.elem "docker" userGroups))} == true ]]
      [[ ${lib.escapeShellArg (lib.boolToString (builtins.elem "render" userGroups))} == true ]]
      [[ ${lib.escapeShellArg (lib.boolToString (builtins.elem "video" userGroups))} == true ]]
      touch "$out"
    '';
in
{
  inherit
    devShell
    devTools
    doctor
    setup
    ;

  checks = {
    formatting = formatCheck;
    scripts = scriptsCheck;
    toolchain = toolchainCheck;
  }
  // lib.optionalAttrs pkgs.stdenv.hostPlatform.isLinux {
    nixos-module = nixosModuleCheck;
  };
}
