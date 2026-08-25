{
  config,
  lib,
  ...
}:
let
  cfg = config.prismedia.development;
in
{
  options.prismedia.development = {
    enable = lib.mkEnableOption "the Prismedia development host support";

    users = lib.mkOption {
      type = lib.types.listOf lib.types.str;
      default = [ ];
      example = [ "alice" ];
      description = ''
        Existing NixOS users that should receive the groups required by enabled
        development features. Docker group membership grants root-equivalent
        access to the Docker daemon.
      '';
    };

    docker.enable = lib.mkOption {
      type = lib.types.bool;
      default = true;
      description = "Whether to enable the Docker daemon for the development stack.";
    };

    direnv.enable = lib.mkOption {
      type = lib.types.bool;
      default = true;
      description = "Whether to enable direnv and nix-direnv integration.";
    };

    gpu.enable = lib.mkOption {
      type = lib.types.bool;
      default = false;
      description = ''
        Whether to enable the NixOS graphics stack and grant the listed users
        access to the render and video device groups. Vendor drivers remain a
        host-level choice.
      '';
    };
  };

  config = lib.mkIf cfg.enable (
    lib.mkMerge [
      {
        nix.settings.experimental-features = [
          "nix-command"
          "flakes"
        ];

        # pnpm dependencies such as esbuild ship upstream glibc binaries. nix-ld
        # supplies the standard loader without mutating node_modules.
        programs.nix-ld.enable = true;
      }

      (lib.mkIf cfg.docker.enable {
        virtualisation.docker.enable = lib.mkDefault true;
        users.users = lib.genAttrs cfg.users (_: {
          extraGroups = [ "docker" ];
        });
      })

      (lib.mkIf cfg.direnv.enable {
        programs.direnv.enable = true;
        programs.direnv.nix-direnv.enable = true;
      })

      (lib.mkIf cfg.gpu.enable {
        hardware.graphics.enable = true;
        users.users = lib.genAttrs cfg.users (_: {
          extraGroups = [
            "render"
            "video"
          ];
        });
      })
    ]
  );
}
