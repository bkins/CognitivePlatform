
  // HealthConnect — bridge to the LAA Android app running on the same LAN.
  //   PhoneBaseUrl:       IP and port of the LAA app, e.g. "http://192.168.1.5:5050".
  //                       Leave empty to disable health integration gracefully
  //                       (DisconnectedHealthProvider is used; all health queries return
  //                       a "phone not connected" message instead of an error).
  //   SharedSecret:       Arbitrary secret string. Must equal HealthGateway:SharedSecret
  //                       in the LAA app's appsettings.json. Sent as the X-CP-Key header.
  //   PingTimeoutSeconds: How long to wait for the /health/ping probe before treating
  //                       the phone as offline (IsConnected = false). Default: 4 s.
  ,"HealthConnect": {
  "PhoneBaseUrl": "http://192.168.0.x:5050",
  "SharedSecret": "replace-with-a-strong-random-string",
  "PingTimeoutSeconds": 4
}
  // FileSync — bridge to the LAA Android file gateway running on the same LAN.
  //   GatewayBaseUrl:     IP and port of the LAA file gateway, e.g. "http://192.168.1.5:5051".
  //                       Leave empty to disable file sync gracefully
  //                       (DisconnectedFileSyncProvider is used; all sync queries return
  //                       a "phone not connected" message instead of an error).
  //   SharedSecret:       Arbitrary secret string. Must equal FileSync:SharedSecret
  //                       in the LAA app's appsettings.json. Sent as the X-CP-Key header.
  //   DeviceName:         Human-readable name shown in file sync responses. Default: "Phone".
  //   PingTimeoutSeconds: How long to wait for the /files/ping probe before treating
  //                       the phone as offline (IsConnected = false). Default: 4 s.
  ,"FileSync": {
    "GatewayBaseUrl":    ""
  , "SharedSecret":      ""
  , "DeviceName":        "Phone"
  , "PingTimeoutSeconds": 4
  }
  // Wellbeing — cross-domain signal analysis and proactive check-ins (W.1).
  //   CheckInSuppressionHours:   Minimum hours between wellbeing check-in notifications
  //                              for the same anomaly type. Deduplication is currently
  //                              client-side via stable ExternalId; this value documents
  //                              the intended window for future server-side enforcement.
  //   AnomalySeverityThreshold:  Minimum PatternSeverity that triggers a CheckIn
  //                              notification. "Attention" includes both Attention and
  //                              Concern patterns; "Concern" restricts to Concern only.
  //                              Default: "Attention".
  ,"Wellbeing": {
    "CheckInSuppressionHours":  72
  , "AnomalySeverityThreshold": "Attention"
  }
}

