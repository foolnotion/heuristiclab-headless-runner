{
  description = "HeuristicLab headless runner Mono environment";

  inputs.nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";

  outputs = { self, nixpkgs }:
    let
      systems = [ "x86_64-linux" ];
      forAllSystems = f: nixpkgs.lib.genAttrs systems (system: f nixpkgs.legacyPackages.${system});
    in {
      devShells = forAllSystems (pkgs: {
        default = pkgs.mkShell {
          packages = with pkgs; [
            bash
            coreutils
            git
            mono
            msbuild
          ];

          shellHook = ''
            export HL_INTERPRETER=''${HL_INTERPRETER:-default}
            echo "HeuristicLab headless runner Mono shell"
            echo "Build: scripts/build-linux-mono.sh [path-to-HeuristicLab]"
            echo "Run with HL_INTERPRETER=default on Linux."
          '';
        };
      });

      apps = forAllSystems (pkgs: {
        build-linux-mono = {
          type = "app";
          program = "${pkgs.writeShellScript "build-linux-mono" ''
            exec ${self}/scripts/build-linux-mono.sh "$@"
          ''}";
        };
      });
    };
}
