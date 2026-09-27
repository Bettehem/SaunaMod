#!/bin/sh

if ! dotnet build -c Debug; then
	exit 1
fi

if [ "$1" != "-y" ] && [ "$1" != "-n" ]; then
	printf "Build a release build? [y/N]: "
	read -r answer
fi

if [ "$answer" = "y" ] || [ "$answer" = "Y" ] || [ "$1" = "-y" ]; then
	dotnet build -c Release
fi
