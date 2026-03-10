const BASE = '/api';

export interface Workflow {
  id: string;
  name: string;
  description: string;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
  steps: WorkflowStep[];
  triggers: WorkflowTrigger[];
}

export interface WorkflowStep {
  id: string;
  name: string;
  type: string;
  order: number;
  configuration: string;
  conditionExpression: string | null;
}

export interface WorkflowTrigger {
  id: string;
  eventType: string;
  filterExpression: string | null;
}

export interface WorkflowSummary {
  id: string;
  name: string;
  description: string;
  isActive: boolean;
  stepCount: number;
  triggerCount: number;
  createdAt: string;
}

export interface ExecutionSummary {
  id: string;
  workflowId: string;
  workflowName: string | null;
  status: string;
  currentStepIndex: number;
  totalSteps: number;
  startedAt: string;
  completedAt: string | null;
}

export interface Event {
  id: string;
  type: string;
  payload: string;
  source: string;
  occurredAt: string;
  processed: boolean;
}

async function request<T>(path: string, options?: RequestInit): Promise<T> {
  const res = await fetch(`${BASE}${path}`, {
    headers: { 'Content-Type': 'application/json' },
    ...options,
  });
  if (!res.ok) throw new Error(`${res.status} ${res.statusText}`);
  return res.json();
}

export const api = {
  workflows: {
    list: () => request<WorkflowSummary[]>('/workflows'),
    get: (id: string) => request<Workflow>(`/workflows/${id}`),
    create: (data: { name: string; description: string; steps?: { name: string; type: string; order: number }[]; triggers?: { eventType: string }[] }) =>
      request<Workflow>('/workflows', { method: 'POST', body: JSON.stringify(data) }),
    update: (id: string, data: { name?: string; description?: string; isActive?: boolean }) =>
      request<Workflow>(`/workflows/${id}`, { method: 'PUT', body: JSON.stringify(data) }),
    delete: (id: string) => fetch(`${BASE}/workflows/${id}`, { method: 'DELETE' }),
  },
  executions: {
    list: (workflowId?: string) => request<ExecutionSummary[]>(`/executions${workflowId ? `?workflowId=${workflowId}` : ''}`),
    start: (workflowId: string) => request<unknown>(`/executions/${workflowId}/start`, { method: 'POST', body: 'null' }),
    cancel: (id: string) => fetch(`${BASE}/executions/${id}/cancel`, { method: 'POST' }),
  },
  events: {
    list: () => request<Event[]>('/events'),
    publish: (data: { type: string; payload: string; source: string }) =>
      request<Event>('/events', { method: 'POST', body: JSON.stringify(data) }),
  },
  health: async () => {
    const res = await fetch('/health');
    return res.ok ? 'Healthy' : 'Unhealthy';
  },
};
