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
  title,
  secrets,
  onClose,
}: {
  open: boolean;
  title: string;
  secrets: ReadonlyArray<{ label: string; value: string }> | null;
  onClose: () => void;
}) {
  const [copiedLabel, setCopiedLabel] = useState<string | null>(null);

  const closeDialog = () => {
    setCopiedLabel(null);
    onClose();
  };

  return (
    <Dialog open={open && secrets !== null} onOpenChange={(_, data) => !data.open && closeDialog()}>
      <DialogSurface>
        <DialogBody>
          <DialogTitle>{title}</DialogTitle>
          <DialogContent>
            <MessageBar intent="warning">
              <MessageBarBody>You will not see this value again — copy it now.</MessageBarBody>
            </MessageBar>
            {secrets?.map((secret) => (
              <Field key={secret.label} label={secret.label} style={{ marginTop: 12 }}>
                <Input
                  readOnly
                  value={secret.value}
                  contentAfter={
                    <Button
                      appearance="subtle"
                      aria-label={`Copy ${secret.label}`}
                      icon={<CopyRegular />}
                      onClick={async () => {
                        await navigator.clipboard.writeText(secret.value);
                        setCopiedLabel(secret.label);
                      }}
                    />
                  }
                />
              </Field>
            ))}
            {copiedLabel && <Body1>{copiedLabel} copied to clipboard.</Body1>}
          </DialogContent>
          <DialogActions>
            <Button appearance="primary" onClick={closeDialog}>
              Done — I've saved it
            </Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
}
