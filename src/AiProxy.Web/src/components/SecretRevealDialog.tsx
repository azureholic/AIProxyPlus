import { useState } from 'react';
import {
  Body1,
  Button,
  Dialog,
  DialogActions,
  DialogBody,
  DialogContent,
  DialogSurface,
  DialogTitle,
  Field,
  Input,
  MessageBar,
  MessageBarBody,
} from '@fluentui/react-components';
import { CopyRegular } from '@fluentui/react-icons';

/**
 * Shows a plaintext secret exactly once (consumer API key create/rotate — architecture.md §7.2). Closing this
 * dialog is the only warning the operator gets; the value is never retrievable again after that.
 */
export function SecretRevealDialog({
  open,
  secretLabel,
  secret,
  onClose,
}: {
  open: boolean;
  secretLabel: string;
  secret: string | null;
  onClose: () => void;
}) {
  const [copied, setCopied] = useState(false);

  return (
    <Dialog open={open && secret !== null} onOpenChange={(_, data) => !data.open && onClose()}>
      <DialogSurface>
        <DialogBody>
          <DialogTitle>{secretLabel}</DialogTitle>
          <DialogContent>
            <MessageBar intent="warning">
              <MessageBarBody>You will not see this value again — copy it now.</MessageBarBody>
            </MessageBar>
            <Field label={secretLabel} style={{ marginTop: 12 }}>
              <Input
                readOnly
                value={secret ?? ''}
                contentAfter={
                  <Button
                    appearance="subtle"
                    icon={<CopyRegular />}
                    onClick={async () => {
                      await navigator.clipboard.writeText(secret ?? '');
                      setCopied(true);
                    }}
                  />
                }
              />
            </Field>
            {copied && <Body1>Copied to clipboard.</Body1>}
          </DialogContent>
          <DialogActions>
            <Button appearance="primary" onClick={onClose}>
              Done — I've saved it
            </Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
}
