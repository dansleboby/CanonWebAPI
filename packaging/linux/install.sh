#!/usr/bin/env bash
# Installs or updates CanonWebAPI as a systemd service from an extracted Linux package, or removes it.
#   sudo ./install.sh               install or update (appsettings.Local.json and the logs are kept)
#   sudo ./install.sh --uninstall   remove the service, the udev rule, /opt/canonwebapi and the canonwebapi user
set -euo pipefail

readonly app_dir=/opt/canonwebapi
readonly service=canonwebapi
readonly user=canonwebapi
readonly unit_file=/etc/systemd/system/canonwebapi.service
readonly rule_file=/etc/udev/rules.d/60-canonwebapi.rules
package_dir="$(cd "$(dirname "$0")" && pwd)"
readonly package_dir

fail() {
    echo "Error: $*" >&2
    exit 1
}

# Applies the udev rules to a plugged camera as at boot or replug (60-libgphoto2*.rules skips change events), then
# waits until it has its final ownership.
apply_camera_rules() {
    udevadm trigger --action=add --subsystem-match=usb --attr-match=idVendor=04a9
    udevadm settle
}

uninstall() {
    systemctl disable --now "$service" 2>/dev/null || true
    rm -f "$unit_file" "$rule_file"
    systemctl daemon-reload
    udevadm control --reload
    # Gives a plugged camera its default ownership back.
    apply_camera_rules
    rm -rf "$app_dir"
    if id "$user" >/dev/null 2>&1; then
        userdel "$user"
    fi
    echo "CanonWebAPI removed."
}

install_package() {
    [ "$(uname -m)" = x86_64 ] || fail "this package is for x86_64, not $(uname -m)."
    # No grep -q: with pipefail, grep exiting early would fail the pipeline through SIGPIPE.
    ldconfig -p | grep 'libusb-1.0.so.0' >/dev/null || fail "libusb-1.0 is missing: sudo apt install libusb-1.0-0"
    [ -f "$package_dir/Canon.API" ] || fail "Canon.API not found next to the script: run it from the extracted package."
    [ "$package_dir" != "$app_dir" ] || fail "run the install.sh of the extracted package, not the installed one."

    if ! id "$user" >/dev/null 2>&1; then
        useradd --system --user-group --no-create-home --home-dir /nonexistent --shell /usr/sbin/nologin "$user"
    fi

    systemctl stop "$service" 2>/dev/null || true

    install -d -o root -g root -m 755 "$app_dir"
    install -d -o "$user" -g "$user" -m 755 "$app_dir/logs"

    # Program files only: appsettings.Local.json and the logs of the installation are kept.
    for file in "$package_dir"/*; do
        name="$(basename "$file")"
        case "$name" in
            appsettings.Local.json | canonwebapi.service | 60-canonwebapi.rules) continue ;;
        esac
        [ -f "$file" ] || continue
        install -o root -g root -m 644 "$file" "$app_dir/$name"
    done
    chmod 755 "$app_dir/Canon.API" "$app_dir/install.sh"

    install -o root -g root -m 644 "$package_dir/60-canonwebapi.rules" "$rule_file"
    udevadm control --reload
    apply_camera_rules

    install -o root -g root -m 644 "$package_dir/canonwebapi.service" "$unit_file"
    systemctl daemon-reload
    systemctl enable "$service"
    systemctl restart "$service"

    echo "CanonWebAPI is installed in $app_dir and runs as the $service service: http://localhost:5000/status"
    echo "Settings: $app_dir/appsettings.Local.json. Logs: $app_dir/logs and journalctl -u $service"
    systemctl --no-pager status "$service" || true
}

[ "$(id -u)" -eq 0 ] || fail "run it as root: sudo $0 $*"
command -v systemctl >/dev/null || fail "systemd is required."

case "${1:-}" in
    "") install_package ;;
    --uninstall) uninstall ;;
    *) fail "unknown option $1. Usage: sudo $0 [--uninstall]" ;;
esac
