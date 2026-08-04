import { Checkbox, Text, makeStyles, tokens } from '@fluentui/react-components';
import type { ModelCapability } from '../api/types';

export const modelCapabilities: { value: ModelCapability; label: string }[] = [
  { value: 'Chat', label: 'Chat' },
  { value: 'ImageGeneration', label: 'Image generation' },
  { value: 'Realtime', label: 'Realtime voice' },
  { value: 'SpeechToText', label: 'Speech to text' },
  { value: 'TextToSpeech', label: 'Text to speech' },
];

const useStyles = makeStyles({
  fieldset: {
    display: 'grid',
    gap: tokens.spacingVerticalXS,
    margin: 0,
    padding: 0,
    border: 0,
  },
  legend: {
    padding: 0,
    fontWeight: tokens.fontWeightSemibold,
  },
  options: {
    display: 'grid',
    gridTemplateColumns: 'repeat(auto-fit, minmax(150px, 1fr))',
    gap: tokens.spacingHorizontalXS,
  },
});

export function ModelCapabilityPicker({
  value,
  onChange,
}: {
  value: ModelCapability[];
  onChange: (capabilities: ModelCapability[]) => void;
}) {
  const styles = useStyles();

  return (
    <fieldset className={styles.fieldset}>
      <legend className={styles.legend}>Capabilities</legend>
      <div className={styles.options}>
        {modelCapabilities.map((capability) => (
          <Checkbox
            key={capability.value}
            checked={value.includes(capability.value)}
            label={capability.label}
            onChange={(_, data) =>
              onChange(
                data.checked
                  ? [...value, capability.value]
                  : value.filter((item) => item !== capability.value),
              )
            }
          />
        ))}
      </div>
      {value.length === 0 && <Text role="alert">Select at least one capability.</Text>}
    </fieldset>
  );
}