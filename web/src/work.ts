import { useQuery } from '@tanstack/react-query';
import { get } from './api';

export interface WorkAction { id: string; number: string; title: string; dueDate: string; isOverdue: boolean; assigneeName: string }
export interface Work {
  assigned: WorkAction[]; overdue: WorkAction[]; toVerify: WorkAction[];
  approvals: { id: string; recordType: string; recordId: string; recordNumber: string; stepName: string; submittedAt: string }[];
  waitingOnOthers: { id: string; recordType: string; recordNumber: string; submittedAt: string }[];
}

export const useWork = () => useQuery({ queryKey: ['work'], queryFn: () => get<Work>('/api/my/work') });
