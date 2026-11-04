import { useContext } from 'react';
import { PlanEditContext } from './planEditContext';

export const usePlanEdit = () => useContext(PlanEditContext);
