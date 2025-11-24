{
  description = "Underfall - A Vintage Story code mod";

  inputs = {
    nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";
    flake-utils.url = "github:numtide/flake-utils";
  };

  outputs = { self, nixpkgs, flake-utils, ... }:
    flake-utils.lib.eachDefaultSystem (system:
      let
        pkgs = import nixpkgs {
          inherit system;
          config.allowUnfree = true;
        };

        buildInputs = with pkgs; [
          # .NET SDK
          dotnet-sdk_8

          # Development tools
          omnisharp-roslyn  # LSP for C#
        ];

        nativeBuildInputs = with pkgs; [
          dotnet-sdk_8
        ];
      in
      {
        # Development shell
        devShells.default = pkgs.mkShell {
          inherit buildInputs nativeBuildInputs;

          # Environment variables
          DOTNET_ROOT = "${pkgs.dotnet-sdk_8}";
          DOTNET_CLI_TELEMETRY_OPTOUT = "1";

          shellHook = ''
            echo "Vintage Story Modding development environment"
            echo ".NET version: $(dotnet --version)"
            echo ""
            echo "Available commands:"
            echo "  dotnet build           - Build the mod"
            echo "  dotnet new --list      - List available templates"
            echo "  dotnet new install ... - Install mod templates"
            echo ""

            export VINTAGE_STORY="${pkgs.vintagestory}/share/vintagestory/"
          '';
        };
      });
}
