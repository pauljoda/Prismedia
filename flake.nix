{
  description = "Prismedia reproducible development environment";

  inputs = {
    nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";

    # Keep the Nix browser revision exactly aligned with @playwright/test in
    # package.json. Update this input when the JavaScript dependency changes.
    nixpkgs-playwright.url = "github:NixOS/nixpkgs/9e87430ac7e25a6ba9f5a593c300f4e114a00f57";
  };

  outputs =
    {
      self,
      nixpkgs,
      nixpkgs-playwright,
    }:
    let
      supportedSystems = [
        "x86_64-linux"
        "aarch64-linux"
        "aarch64-darwin"
      ];
      forAllSystems = nixpkgs.lib.genAttrs supportedSystems;
    in
    {
      nixosModules = {
        default = self.nixosModules.development;
        development = import ./nix/nixos-module.nix;
      };

      packages = forAllSystems (
        system:
        let
          environment = import ./nix/environment.nix {
            inherit system nixpkgs nixpkgs-playwright;
          };
        in
        {
          default = environment.devTools;
          doctor = environment.doctor;
          setup = environment.setup;
        }
      );

      apps = forAllSystems (
        system:
        let
          environment = import ./nix/environment.nix {
            inherit system nixpkgs nixpkgs-playwright;
          };
        in
        {
          default = self.apps.${system}.doctor;
          doctor = {
            type = "app";
            program = nixpkgs.lib.getExe environment.doctor;
            meta.description = "Check the Prismedia development environment";
          };
          setup = {
            type = "app";
            program = nixpkgs.lib.getExe environment.setup;
            meta.description = "Install Prismedia workspace dependencies";
          };
        }
      );

      devShells = forAllSystems (
        system:
        let
          environment = import ./nix/environment.nix {
            inherit system nixpkgs nixpkgs-playwright;
          };
        in
        {
          default = environment.devShell;
        }
      );

      checks = forAllSystems (
        system:
        let
          environment = import ./nix/environment.nix {
            inherit system nixpkgs nixpkgs-playwright;
          };
        in
        environment.checks
      );

      formatter = forAllSystems (system: nixpkgs.legacyPackages.${system}.nixfmt);
    };
}
