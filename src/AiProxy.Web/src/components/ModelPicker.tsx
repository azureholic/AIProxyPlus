import { useEffect, useState } from 'react';
import { Combobox, Field, Option, Text } from '@fluentui/react-components';
import { listModels } from '../api/models';
import type { Model, ModelCapability } from '../api/types';

/** Shared across every Playground tab — picks which model to send test traffic to. */
export function ModelPicker({
  value,
  onChange,
  capability,
  label = 'Model',
}: {
  value: string;
  onChange: (name: string, model?: Model) => void;
  capability: ModelCapability;
  label?: string;
}) {
  const [models, setModels] = useState<Model[]>([]);
  const [loaded, setLoaded] = useState(false);

  useEffect(() => {
    listModels()
      .then((allModels) => {
        const matchingModels = allModels.filter((model) => model.capabilities.includes(capability));
        setModels(matchingModels);
        if (value && !matchingModels.some((model) => model.name === value)) {
          onChange('');
        }
        setLoaded(true);
      })
      .catch(() => {
        setModels([]);
        setLoaded(true);
      });
  }, [capability, onChange, value]);

  return (
    <Field label={label}>
      <Combobox
        placeholder="Select a model…"
        value={value}
        selectedOptions={value ? [value] : []}
        onOptionSelect={(_, data) => {
          const name = data.optionValue ?? '';
          onChange(name, models.find((model) => model.name === name));
        }}
      >
        {models.map((model) => (
          <Option key={model.id} value={model.name}>
            {model.name}
          </Option>
        ))}
      </Combobox>
      {loaded && models.length === 0 && <Text size={200}>No configured models support this capability.</Text>}
    </Field>
  );
}
