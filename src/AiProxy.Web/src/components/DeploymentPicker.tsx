import { useEffect, useState } from 'react';
import { Combobox, Field, Option } from '@fluentui/react-components';
import { listModelDeployments } from '../api/modelDeployments';
import type { ModelDeployment } from '../api/types';

/** Shared across every Playground tab — picks which deployment to send test traffic to. */
export function DeploymentPicker({
  value,
  onChange,
  label = 'Model deployment',
}: {
  value: string;
  onChange: (name: string) => void;
  label?: string;
}) {
  const [deployments, setDeployments] = useState<ModelDeployment[]>([]);

  useEffect(() => {
    listModelDeployments()
      .then(setDeployments)
      .catch(() => setDeployments([]));
  }, []);

  return (
    <Field label={label}>
      <Combobox
        placeholder="Select a deployment…"
        value={value}
        selectedOptions={value ? [value] : []}
        onOptionSelect={(_, data) => onChange(data.optionValue ?? '')}
      >
        {deployments.map((deployment) => (
          <Option key={deployment.id} value={deployment.name}>
            {deployment.name}
          </Option>
        ))}
      </Combobox>
    </Field>
  );
}
