# Stage 8B — Android physical smoke check

Run this check on two devices connected to the same private Wi-Fi network.

1. On the receiver, open **Settings → Import data**, choose **Receive from another device**, and verify that a QR, private LAN IP, 12-character fallback code, and expiration are shown.
2. On the sender, open **Settings → Send data**, tap **Scan receiver QR**, grant Camera, scan the receiver QR, and verify the IP and pairing code are populated.
3. Confirm **Send vault** on the sender. On the receiver, verify that transfer stops at confirmation; choose **Confirm import** and verify the app returns to master-password authentication.
4. Create another receiver QR, cancel it, scan the old QR on the sender, and verify that transfer cannot complete.
5. Create a receiver QR, wait three minutes, scan it, and verify that it is rejected as expired.
6. Deny Camera on the sender and verify the app stays usable with the IP and pairing-code manual fallback.
7. Confirm Android screenshot protection remains enabled on the receiver QR screen (`FLAG_SECURE`).

## Privacy/permission inventory

This stage adds only `android.permission.CAMERA`, requested at runtime only after **Scan receiver QR** is tapped. Camera frames are decoded locally; they are not saved, uploaded, copied to the clipboard, or logged. Existing local-network permissions remain `INTERNET`, `ACCESS_NETWORK_STATE`, and `ACCESS_WIFI_STATE`. The Android Debug merged manifest was also checked: it contains those four requested permissions plus the pre-existing generated app-private `com.hillariot.vaultpass.DYNAMIC_RECEIVER_NOT_EXPORTED_PERMISSION` with `signature` protection; it does not add microphone, location, or storage access.

`FUTURE PLATFORM REQUIREMENT`: reassess Android local-network permission requirements when targeting a release where Android explicitly requires one. No additional local-network permission is requested by the current target configuration.
