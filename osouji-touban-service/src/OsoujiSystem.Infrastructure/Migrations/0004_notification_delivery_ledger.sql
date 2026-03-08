CREATE TABLE IF NOT EXISTS notification_channel_deliveries (
    channel_name TEXT NOT NULL,
    notification_id TEXT NOT NULL,
    notification_type TEXT NOT NULL,
    recipient_user_id UUID NOT NULL,
    title TEXT NOT NULL,
    delivered_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT pk_notification_channel_deliveries PRIMARY KEY (channel_name, notification_id)
);

CREATE INDEX IF NOT EXISTS ix_notification_channel_deliveries_recipient_delivered_at
    ON notification_channel_deliveries (recipient_user_id, delivered_at DESC);
