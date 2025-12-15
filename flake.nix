{
  description = "Sparky - Electrical Age for VS";

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
          csharpier         # Code formatter
          imagemagick

          # GTK3 for Sparky.2D GUI
          gtk3
          cairo
          pango
          gdk-pixbuf
          atk
          glib
        ];

        nativeBuildInputs = with pkgs; [
          dotnet-sdk_8
        ];

        # GTK libraries for Sparky.2D GUI
        gtkLibs = with pkgs; [
          gtk3
          cairo
          pango
          gdk-pixbuf
          atk
          glib
        ];

        gtkLibPath = pkgs.lib.makeLibraryPath gtkLibs;
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
            echo "  dotnet test            - Run tests"
            echo "  dotnet run --project Sparky.2D  - Run 2D circuit editor"
            echo "  ./format.sh            - Format all C# files"
            echo "  ./format.sh --check    - Check formatting without changes"
            echo ""

            ${if pkgs.stdenv.isDarwin then ''
	      export VINTAGE_STORY="/Applications/Vintage Story.app/"
              export DYLD_FALLBACK_LIBRARY_PATH="${gtkLibPath}:$DYLD_FALLBACK_LIBRARY_PATH"
            '' else ''
              export VINTAGE_STORY="${pkgs.vintagestory}/share/vintagestory/"
              export LD_LIBRARY_PATH="${gtkLibPath}:$LD_LIBRARY_PATH"
            ''}
          '';
        };
      });
}
