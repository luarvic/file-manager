import { IApprovalRequest } from "./ApprovalRequest";
import { ApprovalStatus } from "./ApprovalStatus";

export interface IApprovalRequestLog {
  id: number;
  approvalRequest: IApprovalRequest;
  who: string;
  when: string;
  whenDate: Date;
  what: string;
}
