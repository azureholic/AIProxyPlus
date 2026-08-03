import { useEffect, useState } from 'react';
import { Combobox, Field, Option } from '@fluentui/react-components';
import { listModels } from '../api/models';
import type { Model } from '../api/types';

/** Shared across every Playground tab — picks which model to send test traffic to. */
export function ModelPicker({
  value,
  onChange,
  label = 'Model',
}: {
  value: string;
  onChange: (name: string) => void;
  label?: string;
}) {
  const [models, setModels] = useState<Model[]>([]);

  useEffect(() => {
    listModels()
      .then(setModels)
      .catch(() => setModels([]));
  }, []);

  return (
    <Field label={label}>
      <Combobox
        placeholder="Select a model…"
        value={value}
        selectedOptions={value ? [value] : []}
        onOptionSelect={(_, data) => onChange(data.optionValue ?? '')}
      >
        {models.map((model) => (
          <Option key={model.id} value={model.name}>
            {model.name}
          </Option>
        ))}
      </Combobox>
    </Field>
  );
}
