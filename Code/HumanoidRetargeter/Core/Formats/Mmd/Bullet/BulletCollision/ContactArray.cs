/*
 *
 * C# / XNA  port of Bullet (c) 2011 Mark Neale <xexuxjy@hotmail.com>
 *
This source file is part of GIMPACT Library.

For the latest info, see http://gimpact.sourceforge.net/

Copyright (c) 2007 Francisco Leon Najera. C.C. 80087371.
email: projectileman@yahoo.com


This software is provided 'as-is', without any express or implied warranty.
In no event will the authors be held liable for any damages arising from the use of this software.
Permission is granted to anyone to use this software for any purpose,
including commercial applications, and to alter it and redistribute it freely,
subject to the following restrictions:

1. The origin of this software must not be misrepresented; you must not claim that you wrote the original software. If you use this software in a product, an acknowledgment in the product documentation would be appreciated but is not required.
2. Altered source versions must be plainly marked as such, and must not be misrepresented as being the original software.
3. This notice may not be removed or altered from any source distribution.
*/
// Adapted from BulletXNA 73b2e1b9: namespace isolation, no XNA rendering, one type per file.
#nullable disable
#pragma warning disable CS1591, CS0168, CS0219, CS0414, CS0660, CS0661, CS0659, CS0693, CS8981, CS0108, CS0114
using System;
using HumanoidRetargeter.Core.Formats.Mmd.Bullet.LinearMath;

namespace HumanoidRetargeter.Core.Formats.Mmd.Bullet.BulletCollision;

internal class ContactArray : ObjectArray<GIM_CONTACT>
{
	public const int MAX_COINCIDENT = 8;
	public ContactArray() : base(64)
	{
	}

	void PushContact(ref IndexedVector3 point, ref IndexedVector3 normal, float depth, int feature1, int feature2)
	{
		Add(new GIM_CONTACT(ref point, ref normal, depth, feature1, feature2));
	}

	void PushContact(ref IndexedVector3 point, ref IndexedVector4 normal, float depth, int feature1, int feature2)
	{
		IndexedVector3 temp = new IndexedVector3(normal.X, normal.Y, normal.Z);
		Add(new GIM_CONTACT(ref point, ref temp, depth, feature1, feature2));
	}

	public void PushTriangleContacts(GIM_TRIANGLE_CONTACT tricontact, int feature1, int feature2)
	{
		for (int i = 0; i < tricontact.m_point_count; i++)
		{
			PushContact(ref tricontact.m_points[i], ref tricontact.m_separating_normal, tricontact.m_penetration_depth, feature1, feature2);
		}
	}

	public void MergeContacts(ContactArray contacts)
	{
		MergeContacts(contacts, true);
	}

	public void MergeContacts(ContactArray contacts, bool normal_contact_average)
	{
		Clear();
		if (contacts.Count == 0)
			return;
		if (contacts.Count == 1)
		{
			Add(contacts[0]);
			return;
		}

		ObjectArray<CONTACT_KEY_TOKEN> keycontacts = new ObjectArray<CONTACT_KEY_TOKEN>();
		keycontacts.Capacity = contacts.Count;
		//fill key contacts
		for (int i = 0; i < contacts.Count; i++)
		{
			keycontacts.Add(new CONTACT_KEY_TOKEN(contacts[i].CalcKeyContact(), i));
		}

		//sort keys
		keycontacts.Sort(new Comparison<CONTACT_KEY_TOKEN>(CONTACT_KEY_TOKEN.SortPredicate));
		// Merge contacts
		int coincident_count = 0;
		IndexedVector3[] coincident_normals = new IndexedVector3[MAX_COINCIDENT];
		uint last_key = keycontacts[0].m_key;
		uint key = 0;
		Add(contacts[keycontacts[0].m_value]);
		GIM_CONTACT pcontact = this[0];
		int pcontactIndex = 0;
		for (int i = 1; i < keycontacts.Count; i++)
		{
			key = keycontacts[i].m_key;
			GIM_CONTACT scontact = contacts[keycontacts[i].m_value];
			if (last_key == key) //same points
			{
				//merge contact
				if (pcontact.m_depth - GIM_CONTACT.CONTACT_DIFF_EPSILON > scontact.m_depth) //)
				{
					//*pcontact = *scontact;
					this[pcontactIndex] = scontact;
					coincident_count = 0;
				}
				else if (normal_contact_average)
				{
					if (Math.Abs(pcontact.m_depth - scontact.m_depth) < GIM_CONTACT.CONTACT_DIFF_EPSILON)
					{
						if (coincident_count < MAX_COINCIDENT)
						{
							coincident_normals[coincident_count] = scontact.m_normal;
							coincident_count++;
						}
					}
				}
			}
			else
			{ //add new contact
				if (normal_contact_average && coincident_count > 0)
				{
					pcontact.InterpolateNormals(coincident_normals, coincident_count);
					coincident_count = 0;
				}

				Add(scontact);
				pcontactIndex = Count - 1;
				pcontact = this[pcontactIndex];
			}

			last_key = key;
		}
	}

	public void MergeContactsUnique(ContactArray contacts)
	{
		Clear();
		if (contacts.Count == 0)
			return;
		if (contacts.Count == 1)
		{
			Add(contacts[0]);
			return;
		}

		GIM_CONTACT average_contact = contacts[0];
		for (int i = 1; i < contacts.Count; i++)
		{
			average_contact.m_point += contacts[i].m_point;
			average_contact.m_normal += contacts[i].m_normal * contacts[i].m_depth;
		}

		// can't see how this does anything....
		//divide
		float divide_average = 1.0f / ((float)contacts.Count);
		average_contact.m_point *= divide_average;
		average_contact.m_normal *= divide_average;
		average_contact.m_depth = average_contact.m_normal.Length();
		average_contact.m_normal /= average_contact.m_depth;
	}
}